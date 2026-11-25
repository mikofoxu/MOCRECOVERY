using System.Text;
using System.Text.Json;
namespace MocRecovery;

public sealed record CompanionResult(int Expressions,int Motions,int Poses,List<string> Notes);
public static class CompanionExporter
{
    static readonly JsonSerializerOptions Pretty=new(){WriteIndented=true};
    public static CompanionResult Export(Plan plan,JsonElement model,string destination)
    {
        Directory.CreateDirectory(destination);
        var parameters=model.GetProperty("parameters").EnumerateArray().ToDictionary(p=>p.GetProperty("id").GetString()!);
        var parts=model.GetProperty("parts").EnumerateArray().Select(p=>p.GetProperty("id").GetString()!).ToHashSet();
        var notes=new List<string>();var inventory=new List<object>();int expressions=0,motions=0,poses=0,index=0;
        foreach(var file in plan.Assets.Order(StringComparer.OrdinalIgnoreCase))
        {
            var name=Path.GetFileName(file);string kind=name.EndsWith(".exp3.json",StringComparison.OrdinalIgnoreCase)?"expression":name.EndsWith(".motion3.json",StringComparison.OrdinalIgnoreCase)?"motion":name.EndsWith(".pose3.json",StringComparison.OrdinalIgnoreCase)?"pose":"";
            if(kind=="")continue;
            string stem=$"{++index:D3}-"+SafeName(name);
            try
            {
                using var document=JsonDocument.Parse(File.ReadAllBytes(file));var root=document.RootElement;
                if(kind=="expression")
                {
                    var values=new Dictionary<string,double>();var seen=new HashSet<string>();
                    foreach(var p in root.GetProperty("Parameters").EnumerateArray())
                    {
                        string id=p.GetProperty("Id").GetString()!;double v=Number(p.GetProperty("Value"));
                        if(!seen.Add(id))throw new InvalidDataException("Duplicate expression parameter: "+id);
                        string blend=p.TryGetProperty("Blend",out var b)?b.GetString()!:"Add";
                        if(blend is not ("Add" or "Multiply" or "Overwrite"))throw new InvalidDataException("Unknown expression blend: "+blend);
                        if(!parameters.TryGetValue(id,out var param)){notes.Add($"{name}: omitted unknown parameter {id}.");continue;}
                        double d=Number(param.GetProperty("default"));double result=blend switch{"Add"=>d+v,"Multiply"=>d*v,_=>v};
                        double clamped=Math.Clamp(result,Number(param.GetProperty("minimum")),Number(param.GetProperty("maximum")));
                        if(result!=clamped)notes.Add($"{name}: {id} snapshot clamped from {result} to {clamped}.");
                        values[id]=clamped;
                    }
                    if(values.Count==0)throw new InvalidDataException("No known expression parameters.");
                    // Include defaults so importing a snapshot does not retain another expression's values.
                    var all=parameters.ToDictionary(p=>p.Key,p=>Number(p.Value.GetProperty("default")));
                    foreach(var pair in values)all[pair.Key]=pair.Value;
                    string output=stem+".snapshot.motion3.json";
                    WriteSnapshot(Path.Combine(destination,output),all.Select(p=>("Parameter",p.Key,p.Value)));
                    inventory.Add(new{source=name,type=kind,output,semantics="Full-weight isolated expression applied to MOC3 defaults; no fades or stacking"});expressions++;
                }
                else if(kind=="motion")
                {
                    ValidateMotion(root,parameters.Keys.ToHashSet(),parts,notes,name);
                    string output=stem;File.Copy(file,Path.Combine(destination,output));
                    inventory.Add(new{source=name,type=kind,output,semantics="Unmodified motion curves; import on recovered model track"});motions++;
                }
                else
                {
                    int groupIndex=0;
                    foreach(var group in root.GetProperty("Groups").EnumerateArray())
                    {
                        groupIndex++;var members=group.EnumerateArray().ToArray();
                        var mapped=new List<(string Id,string[] Links)>();var owned=new HashSet<string>();
                        foreach(var member in members)
                        {
                            string id=member.GetProperty("Id").GetString()!;
                            var links=member.TryGetProperty("Link",out var l)?l.EnumerateArray().Select(e=>e.GetString()!).ToArray():Array.Empty<string>();
                            foreach(var part in new[]{id}.Concat(links))
                            {
                                if(!parts.Contains(part))throw new InvalidDataException("Unknown pose part: "+part);
                                if(!owned.Add(part))throw new InvalidDataException("Ambiguous repeated pose part: "+part);
                            }
                            mapped.Add((id,links));
                        }
                        for(int selected=0;selected<mapped.Count;selected++)
                        {
                            var values=new List<(string,string,double)>();
                            for(int i=0;i<mapped.Count;i++)foreach(string id in new[]{mapped[i].Id}.Concat(mapped[i].Links))values.Add(("PartOpacity",id,i==selected?1:0));
                            string output=$"{stem}.group{groupIndex}-choice{selected+1}.motion3.json";
                            WriteSnapshot(Path.Combine(destination,output),values);
                            inventory.Add(new{source=name,type=kind,output,semantics="Isolated group visibility preview; runtime fading and automatic switching are not recreated"});poses++;
                        }
                    }
                }
            }
            catch(Exception e) when(e is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentException or IOException)
            {notes.Add($"COMPANION NOT FULLY CONVERTED: {name}: {e.Message} Original remains in recovery ZIP.");}
        }
        File.WriteAllText(Path.Combine(destination,"index.json"),JsonSerializer.Serialize(inventory,Pretty));
        File.WriteAllText(Path.Combine(destination,"README.txt"),"EDITOR IMPORT PACKAGE\n\nOpen the recovered CMO3 in Cubism Editor, add it to an animation scene, right-click its model track and choose Import Motion. Choose one of the motion3.json files here. Use a separate scene for each snapshot.\n\nExpressions are full-weight snapshots applied to MOC3 defaults, with Add/Multiply/Overwrite resolved and values clamped to parameter ranges. These are not live expression controllers: original fades, stacking and dependence on an animated base pose are not reproduced.\n\nPose files become separate part-opacity previews per group/choice, including linked parts. Runtime pose selection/fading is not baked. Existing motion files retain their original bytes, fades and events, but Cubism import may not reproduce runtime events, sounds or model-level effects. Original companions remain under original/ in the recovery ZIP. No CAN3 scene or native expression/pose controller is generated.\n\nImport instructions: https://docs.live2d.com/en/cubism-editor-manual/export-moc3-motion3-files/\n\n"+string.Join("\n",notes),Encoding.UTF8);
        return new(expressions,motions,poses,notes);
    }
    static string SafeName(string s)=>string.Concat(s.Select(c=>char.IsLetterOrDigit(c)||c is '.' or '-' or '_'?c:'_'));
    public static double Number(JsonElement e){double v=e.GetDouble();if(!double.IsFinite(v))throw new InvalidDataException("Non-finite number.");return v;}
    public static void WriteSnapshot(string path,IEnumerable<(string Target,string Id,double Value)> values)
    {
        var curves=values.Select(p=>new{Target=p.Target,Id=p.Id,Segments=new[]{0.0,p.Value,0.0,1.0,p.Value}}).ToArray();
        File.WriteAllText(path,JsonSerializer.Serialize(new{Version=3,Meta=new{Duration=1.0,Fps=30.0,Loop=false,AreBeziersRestricted=true,CurveCount=curves.Length,TotalSegmentCount=curves.Length,TotalPointCount=curves.Length*2,UserDataCount=0,TotalUserDataSize=0},Curves=curves,UserData=Array.Empty<object>()},Pretty));
    }
    public static void ValidateMotion(JsonElement root,HashSet<string> parameters,HashSet<string> parts,List<string> notes,string name)
    {
        if(root.GetProperty("Version").GetInt32()!=3)throw new InvalidDataException("Unsupported motion version.");
        var meta=root.GetProperty("Meta");double duration=Number(meta.GetProperty("Duration"));
        if(duration<0||Number(meta.GetProperty("Fps"))<=0)throw new InvalidDataException("Invalid motion duration/FPS.");
        int segments=0,points=0,count=0;var seen=new HashSet<string>();
        foreach(var curve in root.GetProperty("Curves").EnumerateArray())
        {
            count++;string target=curve.GetProperty("Target").GetString()!,id=curve.GetProperty("Id").GetString()!;
            if(!seen.Add(target+"\0"+id))throw new InvalidDataException("Duplicate motion target: "+id);
            if(target=="Parameter"&&!parameters.Contains(id)||target=="PartOpacity"&&!parts.Contains(id))notes.Add($"{name}: motion target {target}/{id} is absent from recovered model.");
            if(target is not ("Parameter" or "PartOpacity"))notes.Add($"{name}: {target}/{id} requires Editor/runtime support; it is retained without baking.");
            var v=curve.GetProperty("Segments").EnumerateArray().Select(Number).ToArray();
            if(v.Length<2||v[0]<0||v[0]>duration+0.0001)throw new InvalidDataException("Invalid first motion point.");
            double last=v[0];points++;int cursor=2;
            while(cursor<v.Length)
            {
                double code=v[cursor++];if(code!=Math.Truncate(code)||code<0||code>3)throw new InvalidDataException("Unknown motion segment.");
                int n=code==1?6:2;if(cursor+n>v.Length)throw new InvalidDataException("Truncated motion segment.");
                double end=v[cursor+n-2];if(end<last||end>duration+0.0001)throw new InvalidDataException("Motion times out of order or beyond duration.");
                if(code==1 && (v[cursor]<last||v[cursor+2]<v[cursor]||v[cursor+2]>end))throw new InvalidDataException("Non-monotonic Bezier time controls.");
                last=end;cursor+=n;segments++;points+=n/2;
            }
        }
        foreach(var (field,actual) in new[]{("CurveCount",count),("TotalSegmentCount",segments),("TotalPointCount",points)})
            if(meta.TryGetProperty(field,out var v)&&v.GetInt32()!=actual)throw new InvalidDataException(field+" does not match motion data.");
    }
}
