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
        var comparer=StringComparer.OrdinalIgnoreCase;
        var names=byGuid.Keys.ToDictionary(k=>k,k=>new HashSet<string>(comparer));
        var directParents=new HashSet<string>();
        foreach(var mesh in doc.Descendants("CArtMeshSource").Where(e=>e.HasElements))
        {
            string name=(mesh.Descendants("s").SingleOrDefault(e=>(string?)e.Attribute("xs.n")=="localName")?.Value??"").Trim();
            string? parent=Ref(mesh,"targetDeformerGuid");var visited=new HashSet<string>();
            if(parent!=null)directParents.Add(parent);
            while(parent!=null&&byGuid.TryGetValue(parent,out var deformer))
            {
                if(!visited.Add(parent))throw new InvalidDataException("Cycle in deformer hierarchy while deriving display names.");
                names[parent].Add(name);parent=Ref(deformer,"targetDeformerGuid");
            }
        }
        // Count all plausible owners before assigning labels, including ancestors.
        // This prevents traversal order from deciding which deformer gets a shared name.
        var contested=names.Values.Where(set=>set.Count==1).Select(set=>set.Single())
            .GroupBy(name=>name,comparer).Where(group=>group.Count()>1).Select(group=>group.Key).ToHashSet(comparer);
        static XElement Label(XElement source)=>source.Descendants("s").Single(e=>(string?)e.Attribute("xs.n")=="localName");
        static string Id(XElement source)=>(string)source.Descendants("CDeformerId").Single().Attribute("idstr")!;
        var reserved=sources.SelectMany(source=>new[]{Id(source).Trim(),Label(source).Value.Trim()}).ToHashSet(comparer);
        var notes=new List<string>();int changed=0;
        foreach(var (guid,source) in byGuid)
        {
            var candidates=names[guid];string id=Id(source);
            var label=Label(source);
            string candidate=candidates.Count==1?candidates.Single():"";
            string? reason=null;
            if(label.Value!=id || !Placeholder(id,"Warp","Rotation","Deformer"))
                reason="already has a descriptive name or ID";
            else if(candidates.Count>1)reason="controls differently named or unnamed ArtMeshes";
            else if(!directParents.Contains(guid))reason="no directly controlled ArtMesh";
            else if(candidate.Length==0 || BroadLabels.Contains(candidate) || Placeholder(candidate,"ArtMesh","Mesh","Layer","Drawable","Object") ||
                !candidate.Any(char.IsLetter))reason="mesh label is blank or generic";
            else if(contested.Contains(candidate)||reserved.Contains(candidate))reason="candidate label is shared or already in use: "+candidate;
            if(reason!=null){notes.Add($"DEFORMER NAME RETAINED: {id} {reason}.");continue;}
            label.Value=candidate;
            changed++;
            notes.Add($"INFERRED DEFORMER NAME: {id} → {candidate} (unique controlled ArtMesh label).");
        }
        notes.Insert(0,$"Deformer display names derived from ArtMeshes: {changed}; retained: {sources.Length-changed}. Stored IDs are preserved.");
        return notes;
    }
    // Category-only labels describe a region, not a distinct deformer role.
    static readonly HashSet<string> BroadLabels=new(StringComparer.OrdinalIgnoreCase)
        {"face","head","body","hair","model","group","parts","part","artwork","default","unnamed"};

    static bool Placeholder(string value,params string[] prefixes)
    {
        foreach(string prefix in prefixes)
        {
            if(!value.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))continue;
            string suffix=value[prefix.Length..].TrimStart(' ','_','-');
            if(suffix.All(char.IsAsciiDigit))return true;
        }
        return false;
    }

    public static string Apply(XDocument doc,string mocPath)
    {
        try
        {
            var bytes=File.ReadAllBytes(mocPath);
            var ids=ReadIds(bytes);
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
            string warpNote;
            try
            {
                var flags = WarpTransforms.Read(bytes, ids.Length);
                var fields = new List<(XElement Field, bool Value)>();
                foreach (var (source, _, index) in changes)
                {
                    bool warp = source.Name == "CWarpDeformerSource";
                    if (warp != flags[index].HasValue)
                        throw new InvalidDataException("Editor and runtime deformer kinds differ.");
                    if (warp)
                        fields.Add((source.Elements("b").Single(e => (string?)e.Attribute("xs.n") == "isQuadTransform"), flags[index]!.Value));
                }
                foreach (var (field, value) in fields) field.Value = XmlConvert.ToString(value);
                warpNote = $"Warp transform modes restored: {fields.Count(f => f.Value)} quad, {fields.Count(f => !f.Value)} legacy.";
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException)
            {
                warpNote = "WARP TRANSFORM MODES NOT RESTORED: " + e.Message;
            }
            return $"Stored deformer IDs preserved as IDs and display names: {ids.Length}. Stored IDs are the initial display labels; the later ArtMesh naming pass may change display labels only. {warpNote}";
        }
        catch(Exception e) when(e is InvalidDataException or OverflowException or DecoderFallbackException or XmlException or InvalidOperationException or KeyNotFoundException)
        {return "DEFORMER IDS NOT RESTORED: "+e.Message+" Generated names retained.";}
    }
}
