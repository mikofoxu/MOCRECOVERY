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
if(args.Length==0)return;
if(args.Length is not (4 or 5))throw new ArgumentException("Optional arguments: PSD, MOC3, engine, empty output directory, optional true/false artwork matching");
var plan=Recovery.Inspect(args[1],args[3]);plan.PsdInput=Path.GetFullPath(args[0]);if(args.Length==5)plan.MatchPsdArtwork=bool.Parse(args[4]);
var report=await Recovery.ConvertAsync(plan,Path.GetFullPath(args[2]),false);
Check(report.Contains("source images imported:"),"PSD import reported");
var archive=CaffArchive.Read(plan.Output);var xml=XDocument.Load(new MemoryStream(archive["main.xml"]));ProjectEnricher.Validate(xml);
Check(xml.Descendants("CModelImageGroup").Any(g=>g.Elements("s").Any(s=>(string?)s.Attribute("xs.n")=="groupName"&&s.Value=="PSD source artwork")),"native PSD source artwork group");
using var zip=ZipFile.OpenRead(plan.Backup);
Check(zip.Entries.Any(e=>e.FullName.StartsWith("original/source-artwork/")),"original PSD preserved");
Check(zip.GetEntry("editor-import/psd-source/layers.json")!=null,"layer inventory exported");
var matching=zip.GetEntry("editor-import/psd-source/artwork-matches.json");
Check((matching!=null)==plan.MatchPsdArtwork,"artwork matching option controls analysis output");
if(plan.MatchPsdArtwork&&Path.GetFileNameWithoutExtension(args[1])=="RedFoxGirl")
{
 using var data=JsonDocument.Parse(matching!.Open());var matches=data.RootElement.GetProperty("matches").EnumerateArray().ToArray();
 Check(matches.Single(m=>m.GetProperty("MeshId").GetString()=="ArtMesh").GetProperty("LayerName").GetString()=="mad sign","real PSD resolves ArtMesh to mad sign");
 Check(matches.Where(m=>m.GetProperty("MeshId").GetString() is "fronthair" or "fronthair2").All(m=>m.GetProperty("Status").GetString()!.StartsWith("Ambiguous")),"real duplicate hair artwork remains ambiguous");
 var mesh=xml.Descendants("CArtMeshSource").Single(e=>e.HasElements&&(string?)e.Descendants("CDrawableId").Single().Attribute("idstr")=="ArtMesh");
 Check(mesh.Descendants("s").Single(e=>(string?)e.Attribute("xs.n")=="localName").Value=="mad sign","artwork match changes display label, retains ArtMesh ID");
}
Console.WriteLine("PSD CHECKS PASSED");
