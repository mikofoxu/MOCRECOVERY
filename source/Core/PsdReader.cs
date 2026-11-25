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
        int pixels=checked(l.Width*l.Height);var rgba=new byte[checked(pixels*4)];for(int i=3;i<rgba.Length;i+=4)rgba[i]=255;
        foreach(var ch in l.Channels.Where(c=>c.Id is >=-1 and <=2))
        {
            stream.Position=ch.Offset;int compression=U16();byte[] data=new byte[pixels];
            if(compression==0)stream.ReadExactly(data);
            else if(compression==1)
            {
                var sizes=new ushort[l.Height];for(int y=0;y<l.Height;y++)sizes[y]=U16();
                for(int y=0;y<l.Height;y++)
                {
                    var row=reader.ReadBytes(sizes[y]);int src=0,dst=y*l.Width,end=dst+l.Width;
                    while(src<row.Length){sbyte code=unchecked((sbyte)row[src++]);if(code>=0){int n=code+1;if(src+n>row.Length||dst+n>end)throw new InvalidDataException("Invalid PSD RLE literal.");row.AsSpan(src,n).CopyTo(data.AsSpan(dst));src+=n;dst+=n;}else if(code!=-128){int n=1-code;if(src>=row.Length||dst+n>end)throw new InvalidDataException("Invalid PSD RLE repeat.");data.AsSpan(dst,n).Fill(row[src++]);dst+=n;}}
                    if(dst!=end)throw new InvalidDataException("Truncated PSD RLE row.");
                }
            }
            else if(compression is 2 or 3)
            {
                var packed=reader.ReadBytes(checked((int)ch.Length-2));using var z=new ZLibStream(new MemoryStream(packed),CompressionMode.Decompress);z.ReadExactly(data);
                if(compression==3)for(int y=0;y<l.Height;y++)for(int x=1;x<l.Width;x++)data[y*l.Width+x]=unchecked((byte)(data[y*l.Width+x]+data[y*l.Width+x-1]));
            }
            else throw new InvalidDataException("Unsupported PSD channel compression.");
            if(stream.Position>ch.Offset+ch.Length)throw new InvalidDataException("PSD channel exceeds declared size.");
            int component=ch.Id==-1?3:ch.Id;for(int i=0;i<pixels;i++)rgba[i*4+component]=data[i];
        }
        if(!l.Channels.Any(c=>c.Id==0)||!l.Channels.Any(c=>c.Id==1)||!l.Channels.Any(c=>c.Id==2))throw new InvalidDataException("Layer has no complete RGB raster.");
        return EncodePng(l.Width,l.Height,rgba,ColorProfile);
    }
    public static byte[] EncodePng(int width,int height,byte[] rgba,byte[]? profile=null)
    {
        using var output=new MemoryStream();output.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string tag,byte[] data){Span<byte> n=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(n,data.Length);output.Write(n);var t=Encoding.ASCII.GetBytes(tag);output.Write(t);output.Write(data);uint crc=0xffffffff;foreach(byte b in t.Concat(data)){crc^=b;for(int k=0;k<8;k++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}BinaryPrimitives.WriteUInt32BigEndian(n,~crc);output.Write(n);}
        var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);header[8]=8;header[9]=6;Chunk("IHDR",header);
        if(profile!=null){using var icc=new MemoryStream();icc.Write(Encoding.ASCII.GetBytes("PSD profile\0\0"));using(var z=new ZLibStream(icc,CompressionLevel.Optimal,true))z.Write(profile);Chunk("iCCP",icc.ToArray());}
        using var packed=new MemoryStream();using(var z=new ZLibStream(packed,CompressionLevel.Fastest,true))for(int y=0;y<height;y++){z.WriteByte(0);z.Write(rgba,y*width*4,width*4);}Chunk("IDAT",packed.ToArray());Chunk("IEND",[]);return output.ToArray();
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
