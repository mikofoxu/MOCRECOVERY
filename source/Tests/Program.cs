using MocRecovery;
using System.IO.Compression;
using System.Text.Json;
static void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);}
if(args.Length!=3)throw new ArgumentException("model folder, engine path, test output directory required");
var dir=Path.GetFullPath(args[2]);Directory.CreateDirectory(dir);
var source=Path.GetFullPath(args[0]);var engine=Path.GetFullPath(args[1]);
foreach(var f in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
{var dst=Path.Combine(dir,Path.GetRelativePath(source,f));Directory.CreateDirectory(Path.GetDirectoryName(dst)!);File.Copy(f,dst,true);}
var model=Path.Combine(dir,"Example.moc3");var outputDir=Path.Combine(dir,"app folder");Directory.CreateDirectory(outputDir);
var p=Recovery.Inspect(model,outputDir);
Check(p.Output==Path.Combine(outputDir,"Example.cmo3"),"same basename in application folder, separate from input");
Check(p.Textures.Count==1 && p.Textures[0].EndsWith("texture_00.png"),"manifest texture ordering");
Check(p.Assets.Any(f=>f.EndsWith(".physics3.json")) && p.Assets.Any(f=>f.EndsWith(".exp3.json")),"physics and unreferenced expression preservation");
var progressValues=new List<int>();
var numeric=new CaptureProgress(v=>{if(v.Percent==100)Check(p.Outputs.All(File.Exists),"100 percent emitted only after outputs exist");progressValues.Add(v.Percent);});
var text=await Recovery.ConvertAsync(p,engine,false,numericProgress:numeric);
Check(progressValues.First()==0&&progressValues.Last()==100&&progressValues.All(v=>v>=0&&v<=100)&&progressValues.Zip(progressValues.Skip(1)).All(p=>p.First<=p.Second),"progress runs from 0 to 100 monotonically");
Check(progressValues.Any(v=>v>=88&&v<98),"archive writing reports intermediate progress");
Check(File.Exists(p.Output)&&File.Exists(p.Report)&&File.Exists(p.Backup),"CMO3, text report and recovery ZIP produced");
Check(text.Contains("Native physics setups restored: 12") && text.Contains("Native parameter folders restored (excluding root): 1"),"native physics and parameter groups restored");
var archiveData=CaffArchive.Read(p.Output);
var xml=System.Xml.Linq.XDocument.Load(new MemoryStream(archiveData["main.xml"]));
ProjectEnricher.Validate(xml);
var blendParameters=xml.Descendants("CParameterSource").Where(e=>e.HasElements&&(string?)e.Element("Type")?.Attribute("v")=="MORPH_TARGET")
    .ToDictionary(e=>(string)e.Element("CParameterGuid")!.Attribute("xs.ref")!,e=>(string)e.Element("CParameterId")!.Attribute("idstr")!);
Check(blendParameters.Count==5,"runtime distinguishes five blend shape sliders");
var lip=xml.Descendants("CWarpDeformerSource").Single(e=>e.HasElements&&(string?)e.Descendants("CDeformerId").Single().Attribute("idstr")=="Warp6");
var endpoints=lip.Descendants("KeyFormMorphTarget").Select(t=>(blendParameters[(string)t.Element("CParameterGuid")!.Attribute("xs.ref")!],t.Element("f")!.Value)).ToArray();
Check(endpoints.Length==6 && endpoints.ToHashSet().SetEquals(from id in new[]{"MouthX","MouthPressLipOpen","MouthPucker"} from key in new[]{"-1","1"} select (id,key)),"six distinct runtime lip endpoints recovered");
Check(archiveData.Keys.Count(k=>k.StartsWith("atlas-mesh-")&&k.EndsWith(".png"))==38,"one embedded atlas cutout per mesh without a PSD");
var storedIds=DeformerNames.ReadIds(File.ReadAllBytes(model));
var deformers=xml.Descendants().Where(e=>e.HasElements&&(e.Name=="CWarpDeformerSource"||e.Name=="CRotationDeformerSource")).ToArray();
Check(deformers.Select(e=>(string)e.Descendants("CDeformerId").Single().Attribute("idstr")!).ToHashSet().SetEquals(storedIds),"all 37 stored deformer IDs preserved");
var tailDeformer=deformers.Single(e=>(string?)e.Descendants("CDeformerId").Single().Attribute("idstr")=="Warp21");
Check(tailDeformer.Descendants("s").Single(s=>(string?)s.Attribute("xs.n")=="localName").Value=="tail","tail mesh names Warp21 without changing its ID");
var namingFixture=new System.Xml.Linq.XDocument(xml);
foreach(var d in namingFixture.Descendants().Where(e=>e.HasElements&&(e.Name=="CWarpDeformerSource"||e.Name=="CRotationDeformerSource")))
 d.Descendants("s").Single(e=>(string?)e.Attribute("xs.n")=="localName").Value=(string)d.Descendants("CDeformerId").Single().Attribute("idstr")!;
var beforeNames=new System.Xml.Linq.XDocument(namingFixture);
var namingNotes=DeformerNames.NameFromArtMeshes(namingFixture);
foreach(var document in new[]{beforeNames,namingFixture})
 foreach(var d in document.Descendants().Where(e=>e.HasElements&&(e.Name=="CWarpDeformerSource"||e.Name=="CRotationDeformerSource")))
  d.Descendants("s").Single(e=>(string?)e.Attribute("xs.n")=="localName").Value="ignored display label";
Check(System.Xml.Linq.XNode.DeepEquals(beforeNames,namingFixture),"ArtMesh naming changes only display labels; IDs, references and rig data unchanged");
Check(namingNotes.Any(n=>n.StartsWith("DEFORMER NAME RETAINED: Warp29 ")),"multi-mesh parent keeps its name when ambiguous");
Check(text.Contains("Stored deformer IDs preserved as IDs and display names: 37"),"deformer naming result reported");
// Replace one ID in a copy to exercise a descriptive UTF-8 name, including XML escaping.
var fixture=File.ReadAllBytes(model);int idOffset=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(fixture.AsSpan(64+11*4,4));
fixture.AsSpan(idOffset,64).Clear();System.Text.Encoding.UTF8.GetBytes("Head & 耳").CopyTo(fixture,idOffset);
Check(DeformerNames.ReadIds(fixture)[0]=="Head & 耳","descriptive UTF-8 ID decoded exactly");
var fixturePath=Path.Combine(dir,"named-fixture.moc3");File.WriteAllBytes(fixturePath,fixture);
var rawDoc=System.Xml.Linq.XDocument.Load(new MemoryStream(CaffArchive.Read(Directory.GetFiles(outputDir,"Example.cmo3").Single())["main.xml"]));
// Restore the backend index markers on a test copy, then verify identity rather than traversal order.
foreach(var g in rawDoc.Descendants("CDeformerGuid").Where(e=>e.Attribute("xs.id")!=null))
{int index=System.Array.IndexOf(storedIds,(string?)g.Attribute("note"));if(index>=0)g.SetAttributeValue("note","Deformer"+index);}
Check(DeformerNames.Apply(rawDoc,fixturePath).Contains(": 37."),"descriptive ID applied through verified index mapping");
Check(rawDoc.Descendants("CDeformerId").Any(e=>(string?)e.Attribute("idstr")=="Head & 耳"),"descriptive name preserved in XML");
Check(xml.Descendants("CModelSource").Single(e=>e.HasElements).Elements("i").Single(e=>(string?)e.Attribute("xs.n")=="targetVersionNo").Value=="4020000","Example SDK 4.2 target restored from MOC header");
Check(text.Contains("compatibility target restored from MOC3: 4.2"),"detected SDK target reported");
foreach(var pair in new[]{(1,3000000),(2,3030000),(3,4000000),(4,4020000),(5,5000000),(6,5030000)})Check(SdkTarget.FromMocVersion((byte)pair.Item1).Target==pair.Item2,"MOC version mapping "+pair.Item1);
try{SdkTarget.FromMocVersion(255);throw new Exception("Unknown version accepted");}catch(InvalidDataException){Console.WriteLine("PASS: unknown MOC version rejected");}
Check(xml.Descendants("CPhysicsInput").Count()==26 && xml.Descendants("CPhysicsOutput").Count()==12 && xml.Descendants("CPhysicsVertex").Count()==44,"physics inputs, outputs and particle counts");
Check(xml.Descendants("CParameterGroup").Count(e=>e.HasElements)==2,"root group plus expression folder");
Check(text.Contains("meshes: 38")&&text.Contains("parameters: 49")&&text.Contains("Display names restored from CDI: 50"),"recovered counts and CDI names");
using(var z=ZipFile.OpenRead(p.Backup)){Check(z.Entries.Any(e=>e.FullName.EndsWith(".physics3.json"))&&z.Entries.Any(e=>e.FullName.EndsWith("runtime.model.json")),"ZIP includes original settings and recovered data");}
var hash=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p.Output));
try{await Recovery.ConvertAsync(p,engine,false);throw new Exception("overwrite unexpectedly allowed");}catch(IOException){Console.WriteLine("PASS: unapproved overwrite refused");}
using(var stop=new CancellationTokenSource())
{
 var previous=p.Outputs.Select(path=>System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToArray();
 var checkpoint=new CaptureProgress(update=>{if(update.Percent==98)stop.Cancel();});
 try{await Recovery.ConvertAsync(p,engine,true,numericProgress:checkpoint,cancellationToken:stop.Token);throw new Exception("Cancelled conversion committed.");}
 catch(OperationCanceledException) when(stop.IsCancellationRequested){}
 Check(p.Outputs.Select((path,i)=>System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(previous[i])).All(ok=>ok),"cancellation before commit preserves all three files");
 Check(!Directory.EnumerateDirectories(outputDir,".moc-recovery-*").Any(),"cancellation cleans staging directory");
}
var failedProgress=new List<int>();
try{await Recovery.ConvertAsync(p,"/bin/false",true,numericProgress:new CaptureProgress(v=>failedProgress.Add(v.Percent)));throw new Exception("engine failure unexpectedly succeeded");}catch(InvalidDataException){Console.WriteLine("PASS: failed engine reported");}
Check(!failedProgress.Contains(100),"failed conversion never reports 100 percent");
Check(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p.Output))),"engine failure preserves previous output");
await Recovery.ConvertAsync(p,engine,true);
Check(Directory.GetFiles(outputDir,"Example.cmo3.*.bak").Length==1,"approved replacement retains backup");
var missingDir=Path.Combine(dir,"missing textures");Directory.CreateDirectory(missingDir);
var missingModel=Path.Combine(missingDir,"A.fox.moc3");File.Copy(model,missingModel,true);
var missing=Recovery.Inspect(missingModel,outputDir);missing.Textures.Add(Path.Combine(missingDir,"missing.png"));
var mtext=await Recovery.ConvertAsync(missing,engine,false);
Check(Path.GetFileName(missing.Output)=="A.fox.cmo3" && mtext.Contains("MISSING TEXTURE (transparent placeholder)"),"missing texture placeholder and multi-dot naming");
var bad=Path.Combine(dir,"invalid.moc3");File.WriteAllText(bad,"bad");
try{Recovery.Inspect(bad);throw new Exception("invalid accepted");}catch(InvalidDataException){Console.WriteLine("PASS: invalid MOC rejected");}
try{Recovery.LocalPath(dir,"../outside.png");throw new Exception("traversal accepted");}catch(InvalidDataException){Console.WriteLine("PASS: companion path traversal refused");}
Check(Path.GetDirectoryName(p.Report)==outputDir && Path.GetDirectoryName(p.Backup)==outputDir,"report and preservation ZIP in application folder");
Check(!File.Exists(Path.Combine(dir,"Example.cmo3")),"no generated CMO3 in input folder");
Check(Path.GetDirectoryName(Recovery.Inspect(model).Output)==Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),"default output uses executable base directory");
Check(text.Contains("Inferred linked parameter pairs: 12"),"twelve inferred pairs reported");
var linked=xml.Descendants("CParameterSource").Where(e=>e.HasElements && e.Elements("b").Any(b=>(string?)b.Attribute("xs.n")=="combined" && b.Value=="true")).Select(e=>(string)e.Element("CParameterId")!.Attribute("idstr")!).ToHashSet();
var expected=new[]{"ParamAngleX","ParamEyeBallX","ParamBrowLX","ParamBrowRX","ParamBrowLAngle","ParamBrowRAngle","ParamMouthForm","Param2","Param4","Param6","Param10","ParamBodyAngleX"};
Check(linked.SetEquals(expected),"expected linked controls, including inferred body pair");
p.InferParameterLinks=false;
await Recovery.ConvertAsync(p,engine,true);
var disabled=CaffArchive.Read(p.Output);
var disabledXml=System.Xml.Linq.XDocument.Load(new MemoryStream(disabled["main.xml"]));
Check(!disabledXml.Descendants("CParameterSource").Where(e=>e.HasElements).SelectMany(e=>e.Elements("b")).Any(b=>(string?)b.Attribute("xs.n")=="combined" && b.Value=="true"),"disabled option keeps individual sliders");
Check(archiveData["moc2cmo.model.json"].SequenceEqual(disabled["moc2cmo.model.json"]),"link option leaves recovered runtime geometry and bindings unchanged");
FeatureChecks.Run(dir,p.Output);
AdvancedChecks.Run(dir,p.Output,p.PhysicsInfo!);
Console.WriteLine("ALL CHECKS PASSED");

sealed class CaptureProgress(Action<RecoveryProgress> capture):IProgress<RecoveryProgress>{public void Report(RecoveryProgress value)=>capture(value);}
