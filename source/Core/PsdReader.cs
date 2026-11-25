using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
namespace MocRecovery;
public sealed class PsdLayer
{
    public int Index {get;set;} public string Name {get;set;}=""; public int Top {get;set;} public int Left {get;set;} public int Width {get;set;} public int Height {get;set;}
    public int Section {get;set;} public int? Parent {get;set;} public string Blend {get;set;}="norm"; public int Opacity {get;set;} public bool Visible {get;set;} public bool Clipping {get;set;}
    public List<string> Warnings {get;}=[];
    internal List<(short Id,uint Length,long Offset)> Channels=[];
}
public sealed class PsdReader : IDisposable
{
    readonly FileStream stream;readonly BinaryReader reader;
    public byte[]? ColorProfile {get;private set;}
    public int Width {get;} public int Height {get;} public List<PsdLayer> Layers {get;}=[];
    public PsdReader(string path)
    {
        stream=File.OpenRead(path);reader=new(stream);
        try
        {
            if(Text(4)!="8BPS"||U16()!=1)throw new InvalidDataException("Only standard PSD files (not PSB) are supported.");
            Skip(6);U16();Height=I32();Width=I32();int depth=U16(),mode=U16();
            if(depth!=8||mode!=3)throw new InvalidDataException("PSD import currently requires 8-bit RGB. Save a copy in that format.");
            Skip(U32());long resourceEnd=End(U32());
            while(stream.Position+10<=resourceEnd)
            {
                if(Text(4)!="8BIM")throw new InvalidDataException("Invalid PSD resource signature.");
                int resourceId=U16(),nameLength=reader.ReadByte();Skip(nameLength);Skip((nameLength+1)%2);uint length=U32();long end=End(length);
                if(end>resourceEnd)throw new InvalidDataException("Invalid PSD resource length.");
                if(resourceId==1039)ColorProfile=reader.ReadBytes(checked((int)length));else stream.Position=end;Skip(length%2);
            }
            stream.Position=resourceEnd;long sectionEnd=End(U32());long layerEnd=End(U32());
            if(layerEnd>sectionEnd)throw new InvalidDataException("Invalid PSD layer section.");
            int count=Math.Abs((int)I16());
            for(int i=0;i<count;i++)
            {
                var l=new PsdLayer{Index=i,Top=I32(),Left=I32()};int bottom=I32(),right=I32();l.Height=checked(bottom-l.Top);l.Width=checked(right-l.Left);
                int channels=U16();for(int c=0;c<channels;c++)l.Channels.Add((I16(),U32(),0));
                if(Text(4)!="8BIM")throw new InvalidDataException("Invalid PSD blend signature.");
                l.Blend=Text(4);l.Opacity=reader.ReadByte();l.Clipping=reader.ReadByte()!=0;int flags=reader.ReadByte();l.Visible=(flags&2)==0;reader.ReadByte();
                long extraEnd=End(U32());uint mask=U32();if(mask>0)l.Warnings.Add("Layer mask retained in PSD but not applied to extracted raster.");Skip(mask);Skip(U32());
                int n=reader.ReadByte();l.Name=Encoding.Latin1.GetString(reader.ReadBytes(n));Skip((4-(n+1)%4)%4);
                while(stream.Position+12<=extraEnd)
                {
                    string signature=Text(4),key=Text(4);if(signature is not ("8BIM" or "8B64"))throw new InvalidDataException("Unsupported PSD tagged block.");
                    uint length=U32();long end=End(length);if(end>extraEnd)throw new InvalidDataException("PSD tagged block exceeds layer.");
                    if(key=="luni"){uint chars=U32();if(chars>length/2)throw new InvalidDataException("Invalid Unicode layer name.");l.Name=Encoding.BigEndianUnicode.GetString(reader.ReadBytes(checked((int)chars*2)));}
                    if(key is "lsct" or "lsdk")l.Section=I32();
                    if(key is "SoLd" or "SoLE" or "PlLd")l.Warnings.Add("Smart object: imports its stored raster preview, not editable object contents.");
                    if(key is "lrFX" or "lfx2" or "lfxs" or "vmsk" or "vsms")l.Warnings.Add("Layer effects/vector mask are retained in PSD but not rendered into extracted raster.");
                    stream.Position=end;Skip(length%2);
                }
                stream.Position=extraEnd;
                if(l.Blend!="norm"&&l.Section==0)l.Warnings.Add("Non-normal blend mode is recorded but source preview uses normal blending.");
                if(l.Clipping)l.Warnings.Add("PSD clipping relationship recorded; extracted PNG is the unclipped layer.");
                Layers.Add(l);
            }
            foreach(var l in Layers)for(int c=0;c<l.Channels.Count;c++){var ch=l.Channels[c];l.Channels[c]=(ch.Id,ch.Length,stream.Position);Skip(ch.Length);}
            if(stream.Position>layerEnd)throw new InvalidDataException("PSD channel data exceeds layer section.");
            // PSD records are stored bottom-to-top. Walk top-to-bottom to recover group membership.
            var stack=new Stack<int>();
            for(int i=Layers.Count-1;i>=0;i--)
            {
                var l=Layers[i];if(l.Section==3){if(stack.Count>0)stack.Pop();continue;}
                l.Parent=stack.Count>0?stack.Peek():null;if(l.Section is 1 or 2)stack.Push(i);
            }
        }
        catch{Dispose();throw;}
    }
    public byte[] Png(PsdLayer l)
    {
        if(l.Width<=0||l.Height<=0||l.Width>30000||l.Height>30000||(long)l.Width*l.Height>180_000_000)throw new InvalidDataException("Layer raster dimensions exceed supported limits.");
        var channels=new List<ChannelRows>();
        try
        {
            foreach(var ch in l.Channels.Where(c=>c.Id is >=-1 and <=2))channels.Add(new ChannelRows(stream.Name,ch.Id,ch.Offset,ch.Length,l.Width,l.Height));
            if(!channels.Any(c=>c.Id==0)||!channels.Any(c=>c.Id==1)||!channels.Any(c=>c.Id==2))throw new InvalidDataException("Layer has no complete RGB raster.");
            return EncodeRows(l.Width,l.Height,(y,rgba)=>
            {
                System.Array.Clear(rgba);for(int x=3;x<rgba.Length;x+=4)rgba[x]=255;
                foreach(var channel in channels){channel.ReadRow();int component=channel.Id==-1?3:channel.Id;for(int x=0;x<l.Width;x++)rgba[x*4+component]=channel.Row[x];}
            },ColorProfile);
        }
        finally{foreach(var c in channels)c.Dispose();}
    }
    sealed class ChannelRows : IDisposable
    {
        readonly FileStream file;readonly Stream decoded;readonly int compression;readonly ushort[]? sizes;int row;
        public short Id {get;} public byte[] Row {get;}
        readonly byte[] packed=new byte[65535];
        public ChannelRows(string path,short id,long offset,uint length,int width,int height)
        {
            Id=id;Row=new byte[width];file=File.OpenRead(path);decoded=file;
            try
            {
                if(length<2||offset<0||offset+length>file.Length)throw new InvalidDataException("Invalid PSD channel bounds.");
                file.Position=offset;Span<byte> n=stackalloc byte[2];file.ReadExactly(n);compression=BinaryPrimitives.ReadUInt16BigEndian(n);
                if(compression==0){if((long)width*height>length-2)throw new InvalidDataException("Truncated raw PSD channel.");}
                else if(compression==1)
                {
                    sizes=new ushort[height];long expected=2+height*2L;
                    for(int y=0;y<height;y++){file.ReadExactly(n);sizes[y]=BinaryPrimitives.ReadUInt16BigEndian(n);expected+=sizes[y];}
                    if(expected>length)throw new InvalidDataException("PSD RLE data exceeds channel length.");
                }
                else if(compression is 2 or 3)decoded=new ZLibStream(new LimitedReadStream(file,length-2),CompressionMode.Decompress);
                else throw new InvalidDataException("Unsupported PSD channel compression.");
            }
            catch{file.Dispose();throw;}
        }
        public void ReadRow()
        {
            if(compression==1)
            {
                int length=sizes![row++];file.ReadExactly(packed.AsSpan(0,length));int src=0,dst=0;
                while(src<length)
                {
                    sbyte code=unchecked((sbyte)packed[src++]);
                    if(code>=0){int n=code+1;if(src+n>length||dst+n>Row.Length)throw new InvalidDataException("Invalid PSD RLE literal.");packed.AsSpan(src,n).CopyTo(Row.AsSpan(dst));src+=n;dst+=n;}
                    else if(code!=-128){int n=1-code;if(src>=length||dst+n>Row.Length)throw new InvalidDataException("Invalid PSD RLE repeat.");Row.AsSpan(dst,n).Fill(packed[src++]);dst+=n;}
                }
                if(dst!=Row.Length)throw new InvalidDataException("Truncated PSD RLE row.");
            }
            else
            {
                decoded.ReadExactly(Row);
                if(compression==3)for(int x=1;x<Row.Length;x++)Row[x]=unchecked((byte)(Row[x]+Row[x-1]));
            }
        }
        public void Dispose(){if(decoded!=file)decoded.Dispose();file.Dispose();}
    }
    sealed class LimitedReadStream(Stream source,long remaining) : Stream
    {
        public override int Read(byte[] b,int o,int n){int count=source.Read(b,o,(int)Math.Min(n,remaining));remaining-=count;return count;}
        public override int Read(Span<byte> b){int count=source.Read(b[..(int)Math.Min(b.Length,remaining)]);remaining-=count;return count;}
        public override bool CanRead=>true;public override bool CanWrite=>false;public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
    }
    public static byte[] EncodePng(int width,int height,byte[] rgba,byte[]? profile=null)=>EncodeRows(width,height,(y,row)=>rgba.AsSpan(y*width*4,width*4).CopyTo(row),profile);
    static byte[] EncodeRows(int width,int height,Action<int,byte[]> fill,byte[]? profile)
    {
        using var output=new MemoryStream();output.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string tag,byte[] data,int count)
        {
            Span<byte> n=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(n,count);output.Write(n);var t=Encoding.ASCII.GetBytes(tag);output.Write(t);output.Write(data,0,count);
            uint crc=0xffffffff;void Add(byte b){crc^=b;for(int k=0;k<8;k++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}
            foreach(byte b in t)Add(b);for(int i=0;i<count;i++)Add(data[i]);BinaryPrimitives.WriteUInt32BigEndian(n,~crc);output.Write(n);
        }
        var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);header[8]=8;header[9]=6;Chunk("IHDR",header,header.Length);
        if(profile!=null){using var icc=new MemoryStream();icc.Write(Encoding.ASCII.GetBytes("PSD profile\0\0"));using(var z=new ZLibStream(icc,CompressionLevel.Optimal,true))z.Write(profile);var data=icc.ToArray();Chunk("iCCP",data,data.Length);}
        using(var chunks=new PngChunks((b,n)=>Chunk("IDAT",b,n)))
        {
            using(var z=new ZLibStream(chunks,CompressionLevel.Fastest,true))
            {var row=new byte[checked(width*4)];for(int y=0;y<height;y++){fill(y,row);z.WriteByte(0);z.Write(row);}}
        }
        Chunk("IEND",[],0);return output.ToArray();
    }
    sealed class PngChunks(Action<byte[],int> emit) : Stream
    {
        readonly byte[] buffer=new byte[65536];int used;
        public override void Write(byte[] b,int o,int n)=>Write(b.AsSpan(o,n));
        public override void Write(ReadOnlySpan<byte> data){while(!data.IsEmpty){int n=Math.Min(data.Length,buffer.Length-used);data[..n].CopyTo(buffer.AsSpan(used));used+=n;data=data[n..];if(used==buffer.Length)Flush();}}
        public override void Flush(){if(used>0){emit(buffer,used);used=0;}}
        protected override void Dispose(bool disposing){if(disposing)Flush();base.Dispose(disposing);}
        public override bool CanWrite=>true;public override bool CanRead=>false;public override bool CanSeek=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();
    }
    long End(long size){long end=checked(stream.Position+size);if(size<0||end>stream.Length)throw new InvalidDataException("Truncated PSD section.");return end;}
    void Skip(long n)=>stream.Position=End(n);
    string Text(int n)=>Encoding.ASCII.GetString(reader.ReadBytes(n));
    ushort U16(){Span<byte>b=stackalloc byte[2];stream.ReadExactly(b);return BinaryPrimitives.ReadUInt16BigEndian(b);}
    short I16()=>unchecked((short)U16());
    uint U32(){Span<byte>b=stackalloc byte[4];stream.ReadExactly(b);return BinaryPrimitives.ReadUInt32BigEndian(b);}
    int I32()=>unchecked((int)U32());
    public void Dispose(){reader.Dispose();stream.Dispose();}
}
