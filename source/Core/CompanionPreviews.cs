using System.Text.Json;
using System.Text.Json.Nodes;
namespace MocRecovery;
public static class CompanionPreviews
{
    static readonly JsonSerializerOptions Pretty=new(){WriteIndented=true};
    static double N(JsonElement j)=>CompanionExporter.Number(j);
    public static double Fade(JsonElement root,string field,double fallback)
    {double n=root.TryGetProperty(field,out var f)?N(f):fallback;if(n<0||n>3600)throw new InvalidDataException(field+" must be within 0..3600 seconds for preview generation.");return n;}
    public static void Expression(string path,JsonElement root,Dictionary<string,double> defaults,Dictionary<string,double> targets)
    {
        double fadeIn=Fade(root,"FadeInTime",1),fadeOut=Fade(root,"FadeOutTime",1),duration=fadeIn+1+fadeOut;
        var times=new SortedSet<double>{0,fadeIn,fadeIn+1,duration};
        // A bounded sampled cosine envelope; an isolated preview, not a runtime expression controller.
        foreach(var (start,length) in new[]{(0.0,fadeIn),(fadeIn+1,fadeOut)})
        {int steps=Math.Clamp((int)Math.Ceiling(length*30),1,300);for(int i=1;i<=steps;i++)times.Add(start+length*i/steps);}
        var curves=new List<(string,string,List<(double,double)>)>();
        foreach(var (id,d) in defaults)
        {
            double target=targets.GetValueOrDefault(id,d);var points=new List<(double,double)>();
            foreach(double t in times)
            {
                double weight=t<=fadeIn?(fadeIn==0?1:0.5-0.5*Math.Cos(Math.PI*t/fadeIn)):t<=fadeIn+1?1:fadeOut==0?0:0.5+0.5*Math.Cos(Math.PI*(t-fadeIn-1)/fadeOut);
                points.Add((t,d+(target-d)*weight));
            }
            curves.Add(("Parameter",id,points));
        }
        Write(path,duration,curves);
    }
    public static void Write(string path,double duration,IEnumerable<(string Target,string Id,List<(double Time,double Value)> Points)> curves)
    {
        var data=new List<object>();int segments=0,points=0;
        foreach(var (target,id,values) in curves)
        {
            if(values.Count==0)continue;var v=new List<double>{values[0].Time,values[0].Value};
            foreach(var (t,value) in values.Skip(1))v.AddRange(new[]{0.0,t,value});
            data.Add(new{Target=target,Id=id,Segments=v});segments+=values.Count-1;points+=values.Count;
        }
        File.WriteAllText(path,JsonSerializer.Serialize(new{Version=3,Meta=new{Duration=duration,Fps=30,Loop=false,AreBeziersRestricted=true,CurveCount=data.Count,TotalSegmentCount=segments,TotalPointCount=points,UserDataCount=0,TotalUserDataSize=0},Curves=data,UserData=Array.Empty<object>()},Pretty));
    }
    public static int EditorMotion(string path,JsonElement root,HashSet<string> parameters,HashSet<string> parts)
    {
        var node=JsonNode.Parse(root.GetRawText())!.AsObject();var original=node["Curves"]!.AsArray();var selected=new JsonArray();int removed=0,segments=0,points=0;
        foreach(var c in original)
        {
            string? target=(string?)c!["Target"],id=(string?)c["Id"];
            if(!(target=="Parameter"&&parameters.Contains(id!)||target=="PartOpacity"&&parts.Contains(id!))){removed++;continue;}
            selected.Add(c.DeepClone());var v=c["Segments"]!.AsArray();points++;for(int i=2;i<v.Count;){int code=(int)(double)v[i++]!;int n=code==1?6:2;i+=n;segments++;points+=n/2;}
        }
        node["Curves"]=selected;var meta=node["Meta"]!.AsObject();meta["CurveCount"]=selected.Count;meta["TotalSegmentCount"]=segments;meta["TotalPointCount"]=points;
        // Events and fade metadata stay available; Editor import may not execute runtime events.
        File.WriteAllText(path,node.ToJsonString(Pretty));return removed;
    }
    public static void ValidateExtras(JsonElement root,double duration)
    {
        void FadeCheck(JsonElement e,string key){if(e.TryGetProperty(key,out var v)&&N(v)<0&&N(v)!=-1)throw new InvalidDataException("Invalid "+key+"; expected nonnegative duration or -1 inheritance.");}
        FadeCheck(root,"FadeInTime");FadeCheck(root,"FadeOutTime");
        foreach(var c in root.GetProperty("Curves").EnumerateArray()){FadeCheck(c,"FadeInTime");FadeCheck(c,"FadeOutTime");}
        int count=0,bytes=0;
        if(root.TryGetProperty("UserData",out var data))foreach(var e in data.EnumerateArray())
        {double t=N(e.GetProperty("Time"));if(t<0||t>duration)throw new InvalidDataException("Motion event time is outside duration.");string text=e.GetProperty("Value").GetString()??throw new InvalidDataException("Null motion event.");count++;bytes=checked(bytes+System.Text.Encoding.UTF8.GetByteCount(text));}
        var meta=root.GetProperty("Meta");foreach(var (key,value) in new[]{("UserDataCount",count),("TotalUserDataSize",bytes)})if(meta.TryGetProperty(key,out var v)&&v.GetInt32()!=value)throw new InvalidDataException(key+" does not match motion events.");
    }
}
