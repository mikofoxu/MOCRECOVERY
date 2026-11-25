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
    public static List<string> NameFromArtMeshes(string archivePath)
    {
        var entries=CaffArchive.Read(archivePath);
        var doc=XDocument.Load(new MemoryStream(entries["main.xml"]));
        var notes=NameFromArtMeshes(doc);
        using var ms=new MemoryStream();
        using(var writer=XmlWriter.Create(ms,new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=true,CloseOutput=false}))doc.Save(writer);
        entries["main.xml"]=ms.ToArray();CaffArchive.Write(archivePath,entries);
        return notes;
    }
    public static List<string> NameFromArtMeshes(XDocument doc)
    {
        string? Ref(XElement e,string field)=>(string?)e.Descendants("CDeformerGuid").SingleOrDefault(g=>(string?)g.Attribute("xs.n")==field)?.Attribute("xs.ref");
        var sources=doc.Descendants().Where(e=>e.HasElements&&(e.Name=="CWarpDeformerSource"||e.Name=="CRotationDeformerSource")).ToArray();
        var byGuid=sources.ToDictionary(e=>Ref(e,"guid")??throw new InvalidDataException("Deformer GUID missing."));
        var names=byGuid.Keys.ToDictionary(k=>k,k=>new HashSet<string>(StringComparer.Ordinal));
        foreach(var mesh in doc.Descendants("CArtMeshSource").Where(e=>e.HasElements))
        {
            string? name=mesh.Descendants("s").SingleOrDefault(e=>(string?)e.Attribute("xs.n")=="localName")?.Value;
            if(string.IsNullOrWhiteSpace(name))continue;
            string? parent=Ref(mesh,"targetDeformerGuid");var visited=new HashSet<string>();
            while(parent!=null&&byGuid.TryGetValue(parent,out var deformer))
            {
                if(!visited.Add(parent))throw new InvalidDataException("Cycle in deformer hierarchy while deriving display names.");
                names[parent].Add(name);parent=Ref(deformer,"targetDeformerGuid");
            }
        }
        var notes=new List<string>();int changed=0,ambiguous=0,empty=0;
        foreach(var (guid,source) in byGuid)
        {
            var candidates=names[guid];string id=(string)source.Descendants("CDeformerId").Single().Attribute("idstr")!;
            var label=source.Descendants("s").Single(e=>(string?)e.Attribute("xs.n")=="localName");
            if(candidates.Count==1)
            {
                string name=candidates.Single();if(label.Value!=name){label.Value=name;changed++;notes.Add($"INFERRED DEFORMER NAME: {id} → {name} (from controlled ArtMesh names; ID unchanged).");}
            }
            else if(candidates.Count>1){ambiguous++;notes.Add($"DEFORMER NAME RETAINED: {id} controls differently named ArtMeshes: "+string.Join(", ",candidates.Order(StringComparer.Ordinal))+".");}
            else empty++;
        }
        notes.Insert(0,$"Deformer display names derived from ArtMeshes: {changed}; ambiguous: {ambiguous}; without named ArtMeshes: {empty}. IDs are unchanged. Names are inferred, not recovered original labels.");
        return notes;
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
            return $"Stored deformer IDs preserved as IDs and display names: {ids.Length}. Stored IDs are the initial display labels; the later ArtMesh naming pass may change display labels only.";
        }
        catch(Exception e) when(e is InvalidDataException or OverflowException or DecoderFallbackException or XmlException or InvalidOperationException or KeyNotFoundException)
        {return "DEFORMER IDS NOT RESTORED: "+e.Message+" Generated names retained.";}
    }
}
