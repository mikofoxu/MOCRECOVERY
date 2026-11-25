using MocRecovery;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Globalization;
public static class AdvancedChecks
{
    static void Check(bool ok,string text){if(!ok)throw new Exception("FAIL: "+text);Console.WriteLine("PASS: "+text);}
    static string? Name(XElement e)=>(string?)e.Attribute("xs.n");
    public static void Run(string folder,string cmo,string physics)
    {
        var dir=Path.Combine(folder,"advanced-fixtures");Directory.CreateDirectory(dir);
        var archive=CaffArchive.Read(cmo);var clean=XDocument.Load(new MemoryStream(archive["main.xml"]));
        var baseline=RecoveryValidator.Validate(cmo,Path.Combine(dir,"baseline"),physics);
        Check(baseline.Errors.Count==0&&baseline.Rig?.Deformers==37&&baseline.Rig.Keyforms>0&&baseline.Rig.Samples>0&&baseline.PhysicsSetupsCompared==12,"deformer and source-physics checks accept unmodified recovery");
        using(var sweep=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir,"baseline","parameter-pairs.motion3.json"))))
        {
            using var runtime=JsonDocument.Parse(archive["moc2cmo.model.json"]);
            var ids=runtime.RootElement.GetProperty("parameters").EnumerateArray().Select(p=>p.GetProperty("id").GetString()!).ToHashSet();
            CompanionExporter.ValidateMotion(sweep.RootElement,ids,new(),new(),"pair sweep");
            Check(sweep.RootElement.GetProperty("Curves").GetArrayLength()==ids.Count,"paired review motion validates and includes all parameter defaults");
        }
        void Corrupt(string label,Action<XDocument> change,string expected)
        {
            var xml=new XDocument(clean);change(xml);using var ms=new MemoryStream();xml.Save(ms);archive["main.xml"]=ms.ToArray();
            var path=Path.Combine(dir,"corrupt.cmo3");CaffArchive.Write(path,archive);
            var result=RecoveryValidator.Validate(path,Path.Combine(dir,"corrupt-report"),physics);
            Check(result.Errors.Any(e=>e.Contains(expected)),label);
        }
        Corrupt("changed warp vertex detected",x=>{var a=x.Descendants("CWarpDeformerForm").First().Elements("float-array").Single();var v=a.Value.Split(' ',StringSplitOptions.RemoveEmptyEntries);v[0]=(double.Parse(v[0],CultureInfo.InvariantCulture)+10).ToString(CultureInfo.InvariantCulture);a.Value=string.Join(" ",v);},"keyform 0 values differ");
        Corrupt("changed rotation angle detected",x=>x.Descendants("CRotationDeformerForm").First().SetAttributeValue("angle",123),"keyform 0 values differ");
        Corrupt("changed parent link detected",x=>{var d=x.Descendants("CWarpDeformerSource").First(e=>e.HasElements);var parent=d.Descendants("CDeformerGuid").Single(e=>Name(e)=="targetDeformerGuid");parent.SetAttributeValue("xs.ref",d.Descendants("CDeformerGuid").Single(e=>Name(e)=="guid").Attribute("xs.ref")!.Value);},"parent deformer differs");
        Corrupt("changed deformer grid reference detected",x=>{var source=x.Descendants("CWarpDeformerSource").First(e=>e.HasElements);string gridId=source.Descendants("KeyformGridSource").Single().Attribute("xs.ref")!.Value;var grid=x.Descendants("KeyformGridSource").Single(e=>(string?)e.Attribute("xs.id")==gridId);grid.Descendants("KeyOnParameter").First().Element("i")!.Value="99";},"grid access key differs");
        Corrupt("changed physics scale detected against companion",x=>x.Descendants("CPhysicsOutput").First().Elements("f").Single(e=>Name(e)=="angleScale").Value="12345","physics value differs");
        using(var sample=JsonDocument.Parse("""{"parameters":[{"minimum":-1,"maximum":1,"default":0},{"minimum":-2,"maximum":2,"default":0}],"axes":[{"parameter_index":0,"keys":[-1,0,1]},{"parameter_index":1,"keys":[-2,0,2]}]}"""))
        {
            var r=sample.RootElement;var values=RigChecks.SampleValues(r.GetProperty("axes").EnumerateArray().ToArray(),r.GetProperty("parameters").EnumerateArray().ToArray());
            Check(values.Any(v=>v[0]==-1&&v[1]==2)&&values.Any(v=>v[0]!=0&&v[1]!=0&&Math.Abs(v[0])<1&&Math.Abs(v[1])<2),"simultaneous limit and interior parameter samples present");
        }
        using var model=JsonDocument.Parse("""{"parameters":[{"id":"A","default":2,"minimum":0,"maximum":10}],"parts":[{"id":"Left"},{"id":"Right"},{"id":"Hat"},{"id":"NoHat"}]}""");
        var plan=new Plan{Input="unused",Output="unused"};
        void Add(string n,string text){string f=Path.Combine(dir,n);File.WriteAllText(f,text);plan.Assets.Add(f);}
        Add("fade.exp3.json","""{"FadeInTime":2,"FadeOutTime":1,"Parameters":[{"Id":"A","Value":6,"Blend":"Overwrite"}]}""");
        Add("groups.pose3.json","""{"FadeInTime":0.4,"Groups":[[{"Id":"Left"},{"Id":"Right"}],[{"Id":"Hat"},{"Id":"NoHat"}]]}""");
        Add("mixed.motion3.json","""{"Version":3,"FadeInTime":0.3,"Meta":{"Duration":1,"Fps":30,"CurveCount":3,"TotalSegmentCount":3,"TotalPointCount":6,"UserDataCount":1,"TotalUserDataSize":3},"Curves":[{"Target":"Parameter","Id":"A","FadeOutTime":0.4,"Segments":[0,2,0,1,4]},{"Target":"Parameter","Id":"Missing","Segments":[0,1,0,1,2]},{"Target":"Model","Id":"EyeBlink","Segments":[0,1,0,1,0]}],"UserData":[{"Time":0.5,"Value":"耳"}]}""");
        Add("invalid.pose3.json","""{"Groups":[[{"Id":"Left"}],[{"Id":"DoesNotExist"}]]}""");
        Add("bad-event.motion3.json","""{"Version":3,"Meta":{"Duration":1,"Fps":30},"Curves":[],"UserData":[{"Time":2,"Value":"bad"}]}""");
        var output=Path.Combine(dir,"export");var result=CompanionExporter.Export(plan,model.RootElement,output);
        Check(result.FadePreviews==1&&result.EditorMotions==1&&result.Poses==4,"fade previews, filtered motions and multi-group pose snapshots produced");
        Check(!Directory.GetFiles(output).Any(f=>f.Contains("invalid.pose3")),"invalid later pose group leaves no partial previews");
        Check(result.Notes.Any(n=>n.Contains("bad-event"))&&result.Notes.Any(n=>n.Contains("omits 2")),"invalid events and omitted motion targets reported");
        using(var fade=JsonDocument.Parse(File.ReadAllBytes(Directory.GetFiles(output,"*.fade-preview.motion3.json").Single())))
        {
            var r=fade.RootElement;var v=r.GetProperty("Curves")[0].GetProperty("Segments").EnumerateArray().Select(e=>e.GetDouble()).ToArray();
            double At(double t)=>t==0?v[1]:Enumerable.Range(0,(v.Length-2)/3).Where(i=>Math.Abs(v[3+i*3]-t)<0.000001).Select(i=>v[4+i*3]).Single();
            Check(r.GetProperty("Meta").GetProperty("Duration").GetDouble()==4&&At(0)==2&&Math.Abs(At(1)-4)<1e-8&&At(2)==6&&At(4)==2,"expression fade starts at default, reaches target and returns; midpoint matches envelope");
        }
        using(var pose=JsonDocument.Parse(File.ReadAllBytes(Directory.GetFiles(output,"*group1-choice2.motion3.json").Single())))
        {
            var values=pose.RootElement.GetProperty("Curves").EnumerateArray().ToDictionary(c=>c.GetProperty("Id").GetString()!,c=>c.GetProperty("Segments")[1].GetDouble());
            Check(values["Left"]==0&&values["Right"]==1&&values["Hat"]==1&&values["NoHat"]==0,"pose preview resets other groups consistently");
        }
        using(var motion=JsonDocument.Parse(File.ReadAllBytes(Directory.GetFiles(output,"*.editor.motion3.json").Single())))
        {
            var r=motion.RootElement;Check(r.GetProperty("Curves").GetArrayLength()==1&&r.GetProperty("Curves")[0].GetProperty("FadeOutTime").GetDouble()==0.4&&r.GetProperty("UserData")[0].GetProperty("Value").GetString()=="耳","filtered motion retains known curve, fades and Unicode event");
        }
        foreach(string f in Directory.GetFiles(output,"*.motion3.json")){using var motion=JsonDocument.Parse(File.ReadAllBytes(f));CompanionExporter.ValidateMotion(motion.RootElement,new(){"A"},new(){"Left","Right","Hat","NoHat"},new(),f);}
        Console.WriteLine("ADVANCED CHECKS PASSED");
    }
}
