using MocRecovery;
using System.IO.Compression;
using System.Text.Json;
static void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);}
if(args.Length!=3)throw new ArgumentException("model folder, engine path, test output directory required");
var dir=Path.GetFullPath(args[2]);Directory.CreateDirectory(dir);
var source=Path.GetFullPath(args[0]);var engine=Path.GetFullPath(args[1]);
foreach(var f in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
{var dst=Path.Combine(dir,Path.GetRelativePath(source,f));Directory.CreateDirectory(Path.GetDirectoryName(dst)!);File.Copy(f,dst,true);}
var model=Path.Combine(dir,"RedFoxGirl.moc3");var p=Recovery.Inspect(model);
Check(p.Output==Path.Combine(dir,"RedFoxGirl.cmo3"),"same basename and same folder");
Check(p.Textures.Count==1 && p.Textures[0].EndsWith("texture_00.png"),"manifest texture ordering");
Check(p.Assets.Any(f=>f.EndsWith(".physics3.json")) && p.Assets.Any(f=>f.EndsWith(".exp3.json")),"physics and unreferenced expression preservation");
var text=await Recovery.ConvertAsync(p,engine,false);
Check(File.Exists(p.Output)&&File.Exists(p.Report)&&File.Exists(p.Backup),"CMO3, text report and recovery ZIP produced");
Check(text.Contains("meshes: 38")&&text.Contains("parameters: 49")&&text.Contains("Display names restored from CDI: 50"),"recovered counts and CDI names");
using(var z=ZipFile.OpenRead(p.Backup)){Check(z.Entries.Any(e=>e.FullName.EndsWith(".physics3.json"))&&z.Entries.Any(e=>e.FullName.EndsWith("runtime.model.json")),"ZIP includes original settings and recovered data");}
var hash=System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p.Output));
try{await Recovery.ConvertAsync(p,engine,false);throw new Exception("overwrite unexpectedly allowed");}catch(IOException){Console.WriteLine("PASS: unapproved overwrite refused");}
try{await Recovery.ConvertAsync(p,"/bin/false",true);throw new Exception("engine failure unexpectedly succeeded");}catch(InvalidDataException){Console.WriteLine("PASS: failed engine reported");}
Check(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p.Output))),"engine failure preserves previous output");
await Recovery.ConvertAsync(p,engine,true);
Check(Directory.GetFiles(dir,"RedFoxGirl.cmo3.*.bak").Length==1,"approved replacement retains backup");
var missingDir=Path.Combine(dir,"missing textures");Directory.CreateDirectory(missingDir);
var missingModel=Path.Combine(missingDir,"A.fox.moc3");File.Copy(model,missingModel,true);
var missing=Recovery.Inspect(missingModel);missing.Textures.Add(Path.Combine(missingDir,"missing.png"));
var mtext=await Recovery.ConvertAsync(missing,engine,false);
Check(Path.GetFileName(missing.Output)=="A.fox.cmo3" && mtext.Contains("MISSING TEXTURE (transparent placeholder)"),"missing texture placeholder and multi-dot naming");
var bad=Path.Combine(dir,"invalid.moc3");File.WriteAllText(bad,"bad");
try{Recovery.Inspect(bad);throw new Exception("invalid accepted");}catch(InvalidDataException){Console.WriteLine("PASS: invalid MOC rejected");}
try{Recovery.LocalPath(dir,"../outside.png");throw new Exception("traversal accepted");}catch(InvalidDataException){Console.WriteLine("PASS: companion path traversal refused");}
Console.WriteLine("ALL CHECKS PASSED");
