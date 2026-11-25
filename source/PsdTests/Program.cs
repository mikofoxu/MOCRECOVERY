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
if(args.Length==0)return;
if(args.Length!=4)throw new ArgumentException("Optional arguments: PSD, MOC3, engine, empty output directory");
var plan=Recovery.Inspect(args[1],args[3]);plan.PsdInput=Path.GetFullPath(args[0]);
var report=await Recovery.ConvertAsync(plan,Path.GetFullPath(args[2]),false);
Check(report.Contains("source images imported:"),"PSD import reported");
var archive=CaffArchive.Read(plan.Output);var xml=XDocument.Load(new MemoryStream(archive["main.xml"]));ProjectEnricher.Validate(xml);
Check(xml.Descendants("CModelImageGroup").Any(g=>g.Elements("s").Any(s=>(string?)s.Attribute("xs.n")=="groupName"&&s.Value=="PSD source artwork")),"native PSD source artwork group");
using var zip=ZipFile.OpenRead(plan.Backup);
Check(zip.Entries.Any(e=>e.FullName.StartsWith("original/source-artwork/")),"original PSD preserved");
Check(zip.GetEntry("editor-import/psd-source/layers.json")!=null,"layer inventory exported");
Console.WriteLine("PSD CHECKS PASSED");
