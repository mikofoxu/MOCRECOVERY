using MocRecovery;
using System.Text.Json;
using System.Xml.Linq;
public static class FeatureChecks
{
    static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);Console.WriteLine("PASS: "+label);}
    public static void Run(string folder,string cmo)
    {
        var dir=Path.Combine(folder,"feature-fixtures");Directory.CreateDirectory(dir);
        using var model=JsonDocument.Parse("""{"parameters":[{"id":"A","default":2,"minimum":0,"maximum":10},{"id":"B","default":3,"minimum":0,"maximum":10},{"id":"C","default":1,"minimum":0,"maximum":10}],"parts":[{"id":"Left"},{"id":"Right"},{"id":"Linked"}]}""");
        var plan=new Plan{Input=Path.Combine(dir,"test.moc3"),Output=Path.Combine(dir,"test.cmo3")};
        void Add(string name,string json){var path=Path.Combine(dir,name);File.WriteAllText(path,json);plan.Assets.Add(path);}
        Add("blend.exp3.json","""{"Parameters":[{"Id":"A","Value":3,"Blend":"Add"},{"Id":"B","Value":2,"Blend":"Multiply"},{"Id":"C","Value":8,"Blend":"Overwrite"}]}""");
        Add("choice.pose3.json","""{"Groups":[[{"Id":"Left","Link":["Linked"]},{"Id":"Right"}]]}""");
        // Every supported segment type, with a nontrivial Bezier curve.
        Add("all.motion3.json","""{"Version":3,"Meta":{"Duration":4,"Fps":30,"CurveCount":1,"TotalSegmentCount":4,"TotalPointCount":7},"Curves":[{"Target":"Parameter","Id":"A","Segments":[0,2,0,1,3,1,1.25,4,1.75,5,2,6,2,3,7,3,4,8]}]}""");
        Add("broken.motion3.json","""{"Version":3,"Meta":{"Duration":1,"Fps":30},"Curves":[{"Target":"Parameter","Id":"A","Segments":[0,0,1,0.5]}]}""");
        var output=Path.Combine(dir,"export");var result=CompanionExporter.Export(plan,model.RootElement,output);
        Check(result.Expressions==1&&result.Motions==1&&result.Poses==2,"expression, motion and linked pose conversion counts");
        Check(result.Notes.Any(n=>n.Contains("broken.motion3.json")),"malformed motion explicitly reported");
        var snapshot=Directory.GetFiles(output,"*.snapshot.motion3.json").Single();using var doc=JsonDocument.Parse(File.ReadAllBytes(snapshot));
        var values=doc.RootElement.GetProperty("Curves").EnumerateArray().ToDictionary(c=>c.GetProperty("Id").GetString()!,c=>c.GetProperty("Segments")[1].GetDouble());
        Check(values["A"]==5&&values["B"]==6&&values["C"]==8,"Add Multiply Overwrite apply to defaults correctly");
        foreach(var f in Directory.GetFiles(output,"*.motion3.json"))
        {using var motion=JsonDocument.Parse(File.ReadAllBytes(f));CompanionExporter.ValidateMotion(motion.RootElement,new(){"A","B","C"},new(){"Left","Right","Linked"},new(),f);}
        Check(File.ReadAllBytes(Directory.GetFiles(output,"*all.motion3.json").Single()).SequenceEqual(File.ReadAllBytes(Path.Combine(dir,"all.motion3.json"))),"original motion curves preserved byte-for-byte");
        using var pose=JsonDocument.Parse(File.ReadAllBytes(Directory.GetFiles(output,"*choice1.motion3.json").Single()));
        var op=pose.RootElement.GetProperty("Curves").EnumerateArray().ToDictionary(c=>c.GetProperty("Id").GetString()!,c=>c.GetProperty("Segments")[1].GetDouble());
        Check(op["Left"]==1&&op["Linked"]==1&&op["Right"]==0,"pose choice includes linked-part visibility");
        var archive=CaffArchive.Read(cmo);var xml=XDocument.Load(new MemoryStream(archive["main.xml"]));
        var mesh=xml.Descendants("CArtMeshSource").First(e=>e.HasElements);
        mesh.Descendants("i").First(e=>(string?)e.Attribute("xs.n")=="drawOrder").Value="-12345";
        mesh.Descendants("b").First(e=>(string?)e.Attribute("xs.n")=="invertClippingMask").Value="true";
        using(var ms=new MemoryStream()){xml.Save(ms);archive["main.xml"]=ms.ToArray();}
        var damaged=Path.Combine(dir,"damaged.cmo3");CaffArchive.Write(damaged,archive);
        var validation=RecoveryValidator.Validate(damaged,Path.Combine(dir,"validation"));
        Check(validation.Errors.Any(e=>e.Contains("draw order differs"))&&validation.Errors.Any(e=>e.Contains("inverted clipping")),"validator detects deliberately corrupted draw order and clipping");
        Check(validation.Samples>0&&validation.Keyforms>0,"validator samples deformation keyforms");
        Console.WriteLine("FEATURE CHECKS PASSED");
    }
}
