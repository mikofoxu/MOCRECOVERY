using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MocRecovery;
// CAFF layout independently checked against the supplied original and the
// MIT-licensed moc2cmo archive implementation. No vendor code is executed.
public static class CaffArchive
{
    public static Dictionary<string,byte[]> Read(string path)
    {
        byte[] data=File.ReadAllBytes(path);
        if(data.Length<58 || !data.AsSpan(0,4).SequenceEqual("CAFF"u8))throw new InvalidDataException("Not a CAFF project.");
        int key=BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(14));int pos=54;
        ulong Mask(int n)=>n==8 && key>=0 ? ((ulong)(uint)key<<32)|(uint)key : unchecked((ulong)(long)key)&(n==8?ulong.MaxValue:(1UL<<(n*8))-1);
        ulong Num(int n){if(pos+n>data.Length)throw new InvalidDataException("Truncated CAFF metadata.");ulong v=0;for(int i=0;i<n;i++)v=(v<<8)|data[pos++];return v^Mask(n);}
        string Str(){int n=0;for(int i=0;i<4;i++){int b=(int)Num(1);n=checked((n<<7)|(b&127));if(b<128){if(n>data.Length-pos)throw new InvalidDataException("Invalid CAFF string.");var s=new byte[n];for(int j=0;j<n;j++)s[j]=(byte)(data[pos++]^(byte)key);return Encoding.UTF8.GetString(s);}}throw new InvalidDataException("Invalid CAFF string length.");}
        int count=checked((int)Num(4));if(count>data.Length/24)throw new InvalidDataException("Invalid CAFF entry count.");
        var entries=new List<(string,long,int,int)>();
        for(int i=0;i<count;i++){string name=Str();Str();long offset=checked((long)Num(8));int size=checked((int)Num(4));Num(1);int mode=(int)Num(1);pos=checked(pos+8);if(offset<0||offset>data.Length-size)throw new InvalidDataException("CAFF entry out of bounds.");entries.Add((name,offset,size,mode));}
        var result=new Dictionary<string,byte[]>();
        foreach(var (name,offset,size,mode) in entries)
        {
            byte[] raw=data.AsSpan((int)offset,size).ToArray();for(int i=0;i<raw.Length;i++)raw[i]^=(byte)key;
            if(mode==33)
            {
                try{using var z=new ZipArchive(new MemoryStream(raw));using var s=z.GetEntry("contents")!.Open();using var m=new MemoryStream();s.CopyTo(m);raw=m.ToArray();}
                catch(InvalidDataException)
                {
                    if(raw.Length<46 || !raw.AsSpan(0,4).SequenceEqual("PK\x03\x04"u8) || !raw.AsSpan(raw.Length-16,4).SequenceEqual("PK\x07\x08"u8))throw;
                    int start=30+BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(26))+BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(28));
                    int sizeZ=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(raw.Length-8)));int sizeOut=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(raw.Length-4)));
                    if(start+sizeZ>raw.Length-16)throw new InvalidDataException("Invalid streaming ZIP length.");
                    using var d=new DeflateStream(new MemoryStream(raw,start,sizeZ),CompressionMode.Decompress);using var m=new MemoryStream();d.CopyTo(m);raw=m.ToArray();if(raw.Length!=sizeOut)throw new InvalidDataException("Invalid streaming ZIP output.");
                }
            }
            else if(mode!=16)throw new InvalidDataException("Unsupported CAFF compression.");
            result.Add(name,raw);
        }
        return result;
    }
    public static void Write(string path,Dictionary<string,byte[]> entries)
    {
        const int key=42;using var m=new MemoryStream();
        void Num(ulong value,int n,bool encode=true){ulong mask=n==8?((ulong)key<<32)|key:(ulong)key;if(encode)value^=mask;for(int i=n-1;i>=0;i--)m.WriteByte((byte)(value>>(i*8)));}
        void Str(string s){byte[] b=Encoding.UTF8.GetBytes(s);int n=b.Length;var v=new List<byte>{(byte)(n&127)};while((n>>=7)>0)v.Insert(0,(byte)((n&127)|128));foreach(byte x in v)m.WriteByte((byte)(x^key));foreach(byte x in b)m.WriteByte((byte)(x^key));}
        byte[] header=new byte[54];"CAFF"u8.CopyTo(header);"----"u8.CopyTo(header.AsSpan(7));BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(14),key);header[26]=header[27]=127;m.Write(header);
        Num((ulong)entries.Count,4);var stored=new List<(long,byte[])>();
        foreach(var (name,data) in entries)
        {
            var bytes=data;bool compress=name=="main.xml";
            if(compress){using var zipBytes=new MemoryStream();using(var zip=new ZipArchive(zipBytes,ZipArchiveMode.Create,true)){using var stream=zip.CreateEntry("contents",CompressionLevel.Fastest).Open();stream.Write(bytes);}bytes=zipBytes.ToArray();}
            Str(name);Str(compress?"main_xml":"");long patch=m.Position;Num(0,8);Num((ulong)bytes.Length,4);Num(1,1);Num(compress?33UL:16UL,1);m.Write(new byte[8]);stored.Add((patch,bytes));
        }
        foreach(var (patch,bytes) in stored){long end=m.Position;m.Position=patch;Num((ulong)end,8);m.Position=end;foreach(byte b in bytes)m.WriteByte((byte)(b^key));}
        m.WriteByte(98);m.WriteByte(99);File.WriteAllBytes(path,m.ToArray());
    }
}
