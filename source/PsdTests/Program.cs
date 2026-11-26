using MocRecovery;
using System.IO.Compression;
using System.Xml.Linq;
using System.Text.Json;
static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
using(var fixture=new PsdReader(Path.Combine(AppContext.BaseDirectory,"grouped.psd")))
{
 var inner=fixture.Layers.Single(l=>l.Name=="Inner");var outer=fixture.Layers.Single(l=>l.Name=="Outer");var leaf=fixture.Layers.Single(l=>l.Name=="Unicode 耳");
 Check(inner.Parent==outer.Index&&leaf.Parent==inner.Index&&leaf.Left==-1,"nested groups, Unicode names and negative offsets");
 Check(fixture.Png(leaf).Length>0&&fixture.Png(fixture.Layers.Single(l=>l.Name=="Raw")).Length>0,"raw and ZIP-prediction decoding");
}
ArtworkChecks.Run();
RelinkChecks.Run();
if(args.Length==3)
{
    await MultiplePsdChecks.Run(args[0],args[1],args[2]);
    return;
}
if(args.Length==0)return;
if(args.Length is not (4 or 5 or 6))throw new ArgumentException("Optional arguments: PSD, MOC3, engine, empty output directory, optional true/false artwork matching, optional true/false relinking");
var plan=Recovery.Inspect(args[1],args[3]);plan.PsdInput=Path.GetFullPath(args[0]);if(args.Length>=5)plan.MatchPsdArtwork=bool.Parse(args[4]);if(args.Length==6)plan.RelinkPsdTextures=bool.Parse(args[5]);
var progressValues=new List<int>();
var report=await Recovery.ConvertAsync(plan,Path.GetFullPath(args[2]),false,numericProgress:new PsdProgress(v=>progressValues.Add(v.Percent)));
Check(progressValues.Any(v=>v>30&&v<60)&&progressValues.Last()==100&&progressValues.Zip(progressValues.Skip(1)).All(p=>p.First<=p.Second),"PSD layers advance overall percentage without regression");
Check(report.Contains("source images imported:"),"PSD import reported");
var archive=CaffArchive.Read(plan.Output);var xml=XDocument.Load(new MemoryStream(archive["main.xml"]));ProjectEnricher.Validate(xml);
Check(xml.Descendants("CModelImageGroup").Any(g=>g.Elements("s").Any(s=>(string?)s.Attribute("xs.n")=="groupName"&&s.Value=="PSD source artwork")),"native PSD source artwork group");
using var zip=ZipFile.OpenRead(plan.Backup);
Check(zip.Entries.Any(e=>e.FullName.StartsWith("original/source-artwork/")),"original PSD preserved");
Check(zip.GetEntry("editor-import/psd-source/layers.json")!=null,"layer inventory exported");
var matching=zip.GetEntry("editor-import/psd-source/artwork-matches.json");
Check((matching!=null)==plan.MatchPsdArtwork,"artwork matching option controls analysis output");
if(plan.MatchPsdArtwork&&Path.GetFileNameWithoutExtension(args[1])=="Example")
{
 using var data=JsonDocument.Parse(matching!.Open());var matches=data.RootElement.GetProperty("matches").EnumerateArray().ToArray();
 Check(matches.Single(m=>m.GetProperty("MeshId").GetString()=="ArtMesh").GetProperty("LayerName").GetString()=="mad sign","real PSD resolves ArtMesh to mad sign");
 Check(matches.Where(m=>m.GetProperty("MeshId").GetString() is "fronthair" or "fronthair2").All(m=>m.GetProperty("Status").GetString()!.StartsWith("Ambiguous")),"real duplicate hair artwork remains ambiguous");
 var mesh=xml.Descendants("CArtMeshSource").Single(e=>e.HasElements&&(string?)e.Descendants("CDrawableId").Single().Attribute("idstr")=="ArtMesh");
 Check(mesh.Descendants("s").Single(e=>(string?)e.Attribute("xs.n")=="localName").Value=="mad sign","artwork match changes display label, retains ArtMesh ID");
}
Check((zip.GetEntry("editor-import/psd-source/texture-relinks.json")!=null)==plan.RelinkPsdTextures,"texture relinking option controls analysis output");
var objects=xml.Descendants().Where(e=>e.Attribute("xs.id")!=null).ToDictionary(e=>(string)e.Attribute("xs.id")!);
var expected=new Dictionary<string,int>();
if(plan.RelinkPsdTextures)
{
 using var linked=JsonDocument.Parse(zip.GetEntry("editor-import/psd-source/texture-relinks.json")!.Open());
 foreach(var result in linked.RootElement.GetProperty("results").EnumerateArray())
  if(result.GetProperty("Status").GetString()!.StartsWith("Relinked"))expected[result.GetProperty("MeshId").GetString()!]=result.GetProperty("LayerIndex").GetInt32();
 Check(expected.Count>0,"real project contains verified relinked sources");
}
foreach(var mesh in xml.Descendants("CArtMeshSource").Where(e=>e.HasElements))
{
 string id=(string)mesh.Descendants("CDrawableId").Single().Attribute("idstr")!;
 var extension=objects[(string)mesh.Descendants("CTextureInputExtension").Single().Attribute("xs.ref")!];
 var input=objects[(string)extension.Descendants("CTextureInput_ModelImage").Single().Attribute("xs.ref")!];
 string guid=(string)input.Element("CModelImageGuid")!.Attribute("xs.ref")!;
 var image=xml.Descendants("CModelImage").Single(e=>e.HasElements&&(string?)e.Element("CModelImageGuid")?.Attribute("xs.ref")==guid);
 if(expected.TryGetValue(id,out int layer))
 {
  var resource=objects[(string)image.Elements("CImageResource").Single(e=>(string?)e.Attribute("xs.n")=="_filteredImage").Attribute("xs.ref")!];
  Check(resource.Descendants("file").Single().Attribute("path")!.Value==$"psd-layer-{layer:D4}.png",id+": source reference resolves to the verified PSD layer");
 }
 else Check(image.Elements("s").Single(e=>(string?)e.Attribute("xs.n")=="name").Value==id,id+": unmatched source reference resolves to its own atlas crop");
 Check(extension.Elements().Single(e=>(string?)e.Attribute("xs.n")=="currentTextureInputData").Name=="CTextureInput_TextureAtlasRegion",id+": active atlas input preserved");
}
Console.WriteLine("PSD CHECKS PASSED");

sealed class PsdProgress(Action<RecoveryProgress> capture):IProgress<RecoveryProgress>{public void Report(RecoveryProgress value)=>capture(value);}
