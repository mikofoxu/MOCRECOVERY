using MocRecovery;
using System.Xml.Linq;
static class DeformerNameChecks
{
    public static void Run()
    {
        XElement Deformer(string id,string parent="root")=>new("CWarpDeformerSource",
            new XElement("CDeformerGuid",new XAttribute("xs.n","guid"),new XAttribute("xs.ref",id)),
            new XElement("CDeformerGuid",new XAttribute("xs.n","targetDeformerGuid"),new XAttribute("xs.ref",parent)),
            new XElement("CDeformerId",new XAttribute("idstr",id)),
            new XElement("s",new XAttribute("xs.n","localName"),id));
        XElement Mesh(string parent,string label)=>new("CArtMeshSource",
            new XElement("CDeformerGuid",new XAttribute("xs.n","targetDeformerGuid"),new XAttribute("xs.ref",parent)),
            new XElement("s",new XAttribute("xs.n","localName"),label));
        var doc=new XDocument(new XElement("root",Deformer("Warp70"),Mesh("Warp70","Lantern wick"),
            Deformer("Warp90"),Mesh("Warp90","Cuff"),Deformer("Rotation4"),Mesh("Rotation4"," cuff "),
            Deformer("Keel"),Mesh("Keel","Rudder"),Deformer("Warp81"),Mesh("Warp81","KEEL"),
            Deformer("Warp20"),Mesh("Warp20","ArtMesh47"),Deformer("Warp71"),Mesh("Warp71","Body"),
            Deformer("Warp72"),Mesh("Warp72","Bell"),Mesh("Warp72","Clapper"),
            Deformer("Warp73"),Deformer("Warp74","Warp73"),Mesh("Warp74","Tassel")));
        DeformerNames.NameFromArtMeshes(doc);
        foreach(var source in doc.Descendants("CWarpDeformerSource"))
        {
            string id=(string)source.Element("CDeformerId")!.Attribute("idstr")!;
            if(source.Element("s")!.Value!=(id=="Warp70"?"Lantern wick":id))
                throw new Exception($"Unexpected inferred deformer name for {id}.");
        }
    }
}
