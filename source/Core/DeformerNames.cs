using System.Buffers.Binary;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace MocRecovery;
public static class DeformerNames
{
    public static string[] ReadIds(byte[] bytes)
    {
        if(bytes.Length<704||!bytes.AsSpan(0,4).SequenceEqual("MOC3"u8))throw new InvalidDataException("Invalid MOC3 header.");
        SdkTarget.FromMocVersion(bytes[4]);
        bool big=bytes[5] switch{0=>false,1=>true,_=>throw new InvalidDataException("Invalid MOC3 endianness.")};
        uint Word(int p){if(p<0||p>bytes.Length-4)throw new InvalidDataException("Truncated deformer metadata.");return big?BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(p,4)):BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p,4));}
        int counts=checked((int)Word(64));int count=checked((int)Word(checked(counts+4)));int offset=checked((int)Word(64+11*4));
        if(counts<704||count<0||count>1_000_000||count>0&&(offset<704||(long)offset+count*64L>bytes.Length))throw new InvalidDataException("Deformer ID table is out of bounds.");
        var result=new string[count];var utf8=new UTF8Encoding(false,true);
        for(int i=0;i<count;i++)
        {
            var slot=bytes.AsSpan(offset+i*64,64);int end=slot.IndexOf((byte)0);if(end<0)end=64;
            result[i]=utf8.GetString(slot[..end]);XmlConvert.VerifyXmlChars(result[i]);
            if(string.IsNullOrWhiteSpace(result[i]))throw new InvalidDataException("Empty stored deformer ID.");
        }
        if(result.Distinct(StringComparer.Ordinal).Count()!=count)throw new InvalidDataException("Duplicate stored deformer IDs.");
        return result;
    }
    public static string Apply(XDocument doc,string mocPath)
    {
        try
        {
            var ids=ReadIds(File.ReadAllBytes(mocPath));
            var sources=doc.Descendants().Where(e=>e.HasElements&&(e.Name=="CWarpDeformerSource"||e.Name=="CRotationDeformerSource")).ToArray();
            if(sources.Length!=ids.Length)throw new InvalidDataException("Recovered deformer count differs from ID table.");
            var guids=doc.Descendants("CDeformerGuid").Where(e=>e.Attribute("xs.id")!=null).ToDictionary(e=>(string)e.Attribute("xs.id")!,e=>e);
            var changes=new List<(XElement Source,XElement Guid,int Index)>();var used=new HashSet<int>();
            foreach(var source in sources)
            {
                var guidRef=source.Descendants("CDeformerGuid").Single(e=>(string?)e.Attribute("xs.n")=="guid");
                var guid=guids[(string)guidRef.Attribute("xs.ref")!];string note=(string?)guid.Attribute("note")??"";
                if(!note.StartsWith("Deformer",StringComparison.Ordinal)||!int.TryParse(note[8..],out int index)||index<0||index>=ids.Length||!used.Add(index))throw new InvalidDataException("Cannot verify deformer index mapping.");
                _=source.Descendants("CDeformerId").Single();_=source.Descendants("s").Single(e=>(string?)e.Attribute("xs.n")=="localName");
                changes.Add((source,guid,index));
            }
            foreach(var (source,guid,index) in changes)
            {
                source.Descendants("CDeformerId").Single().SetAttributeValue("idstr",ids[index]);
                source.Descendants("s").Single(e=>(string?)e.Attribute("xs.n")=="localName").Value=ids[index];
                guid.SetAttributeValue("note",ids[index]);
            }
            return $"Stored deformer IDs preserved as IDs and display names: {ids.Length}. Original friendly Editor labels are not inferred.";
        }
        catch(Exception e) when(e is InvalidDataException or OverflowException or DecoderFallbackException or XmlException or InvalidOperationException or KeyNotFoundException)
        {return "DEFORMER IDS NOT RESTORED: "+e.Message+" Generated names retained.";}
    }
}
