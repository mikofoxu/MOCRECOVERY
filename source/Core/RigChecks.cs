using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
namespace MocRecovery;
public sealed record RigCheckResult(int Deformers,int Keyforms,int Samples);
// Checks serialized local rig data, not Cubism's world-space evaluator.
public static class RigChecks
{
    static string? Name(XElement e)=>(string?)e.Attribute("xs.n");
    static double D(string s)=>double.Parse(s,CultureInfo.InvariantCulture);
    static double N(JsonElement j)=>CompanionExporter.Number(j);
    static bool Same(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=Math.Max(0.00002,Math.Max(Math.Abs(a),Math.Abs(b))*0.000002);
    static XElement Field(XElement e,string n)=>e.Descendants().Single(x=>Name(x)==n);
    static string Ref(XElement e,string n)=>(string)(n=="guid"&&(e.Name=="CWarpDeformerSource"||e.Name=="CRotationDeformerSource")?e.Descendants("CDeformerGuid").Single(x=>Name(x)==n):Field(e,n)).Attribute("xs.ref")!;
    public static List<double[]> SampleValues(JsonElement[] axes,JsonElement[] parameters)
    {
        var defaults=axes.Select(a=>N(parameters[a.GetProperty("parameter_index").GetInt32()].GetProperty("default"))).ToArray();
        var result=new List<double[]>();var seen=new HashSet<string>();
        void Add(double[] v){if(result.Count<2048&&seen.Add(string.Join("|",v.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)))))result.Add(v);}
        Add(defaults);
        for(int a=0;a<axes.Length;a++)
        {
            var keys=axes[a].GetProperty("keys").EnumerateArray().Select(N).ToArray();var p=parameters[axes[a].GetProperty("parameter_index").GetInt32()];
            foreach(var v in keys.Concat(keys.Zip(keys.Skip(1)).Select(k=>(k.First+k.Second)/2)).Concat(new[]{N(p.GetProperty("minimum")),N(p.GetProperty("maximum"))}))
            {var t=(double[])defaults.Clone();t[a]=v;Add(t);}
        }
        // Pairwise corners expose interactions missed by single-axis sweeps. Bounded to avoid combinatorial allocation.
        for(int a=0;a<axes.Length;a++)for(int b=a+1;b<axes.Length;b++)
        foreach(var va in new[]{"minimum","maximum"})foreach(var vb in new[]{"minimum","maximum"})
        {var t=(double[])defaults.Clone();t[a]=N(parameters[axes[a].GetProperty("parameter_index").GetInt32()].GetProperty(va));t[b]=N(parameters[axes[b].GetProperty("parameter_index").GetInt32()].GetProperty(vb));Add(t);}
        if(axes.Length>1)for(int s=0;s<32;s++)
        {var t=new double[axes.Length];for(int a=0;a<t.Length;a++){var p=parameters[axes[a].GetProperty("parameter_index").GetInt32()];double fraction=((s*37+a*17+11)%101)/100.0;t[a]=N(p.GetProperty("minimum"))+(N(p.GetProperty("maximum"))-N(p.GetProperty("minimum")))*fraction;}Add(t);}
        return result;
    }
    public static RigCheckResult Validate(JsonElement model,XDocument xml,List<string> errors,List<string> warnings)
    {
        var ds=model.GetProperty("deformers").EnumerateArray().ToArray();var pars=model.GetProperty("parameters").EnumerateArray().ToArray();var bands=model.GetProperty("binding_bands").EnumerateArray().ToArray();
        var objects=xml.Descendants().Where(e=>e.Attribute("xs.id")!=null).ToDictionary(e=>(string)e.Attribute("xs.id")!);
        var sources=xml.Descendants().Where(e=>e.HasElements&&(e.Name=="CWarpDeformerSource"||e.Name=="CRotationDeformerSource")).ToArray();
        var mapped=new Dictionary<int,XElement>();int formsCount=0,samples=0;
        void Check(bool ok,string message){if(!ok)errors.Add(message);}
        // Stable form notes retain backend indices even after display names and stored IDs are restored.
        foreach(var source in sources)
        {
            var form=source.Elements().Single(e=>Name(e)=="keyforms").Descendants("CFormGuid").FirstOrDefault();
            string note=form==null?"":(string?)objects[(string)form.Attribute("xs.ref")!].Attribute("note")??"";
            var first=note.Split(' ')[0];
            if(!first.StartsWith("Deformer")||!int.TryParse(first[8..],out int index)||index<0||index>=ds.Length||!mapped.TryAdd(index,source))errors.Add("Cannot verify recovered deformer index mapping.");
        }
        Check(mapped.Count==ds.Length,"Deformer source count differs.");
        var root=xml.Descendants("CDeformerGuid").Single(e=>(string?)e.Attribute("note")=="ROOT");string rootGuid=(string)root.Attribute("xs.id")!;
        string Parent(JsonElement d)=>d.GetProperty("parent_deformer_index").ValueKind==JsonValueKind.Null?rootGuid:Ref(mapped[d.GetProperty("parent_deformer_index").GetInt32()],"guid");
        var parameterIds=xml.Descendants("CParameterSource").Where(e=>e.HasElements).ToDictionary(e=>(string)e.Element("CParameterGuid")!.Attribute("xs.ref")!,e=>(string)e.Element("CParameterId")!.Attribute("idstr")!);
        foreach(var mesh in model.GetProperty("art_meshes").EnumerateArray())
        {
            var id=mesh.GetProperty("id").GetString();var source=xml.Descendants("CArtMeshSource").SingleOrDefault(e=>e.HasElements&&(string?)e.Descendants("CDrawableId").Single().Attribute("idstr")==id);
            if(source!=null)try{Check(Ref(source,"targetDeformerGuid")==Parent(mesh),id+": parent deformer differs.");}catch(Exception e) when(e is KeyNotFoundException or InvalidOperationException){errors.Add(id+": invalid parent deformer reference.");}
        }
        foreach(var (index,source) in mapped)
        {
            string label="Deformer "+index;var d=ds[index];
            try
            {
                Check(Ref(source,"targetDeformerGuid")==Parent(d),label+": parent deformer differs.");
                var visited=new HashSet<int>{index};var ancestor=d;bool rotationAncestor=false;
                while(ancestor.GetProperty("parent_deformer_index").ValueKind!=JsonValueKind.Null)
                {int p=ancestor.GetProperty("parent_deformer_index").GetInt32();if(!visited.Add(p))throw new InvalidDataException("cycle in hierarchy");ancestor=ds[p];rotationAncestor|=ancestor.GetProperty("kind").GetString()=="rotation";}
                bool warp=d.GetProperty("kind").GetString()=="warp",canvas=d.GetProperty("parent_deformer_index").ValueKind==JsonValueKind.Null;
                Check(source.Name==(warp?"CWarpDeformerSource":"CRotationDeformerSource"),label+": kind differs.");
                double scale=N(model.GetProperty("canvas").GetProperty("pixels_per_unit"));var origin=model.GetProperty("canvas").GetProperty("origin").EnumerateArray().Select(N).ToArray();
                if(warp){Check(D(Field(source,"row").Value)==d.GetProperty("rows").GetInt32()&&D(Field(source,"col").Value)==d.GetProperty("columns").GetInt32(),label+": warp divisions differ.");}
                else Check(Same(D(Field(source,"baseAngle").Value),N(d.GetProperty("base_angle_degrees"))),label+": base angle differs.");
                var forms=d.GetProperty("keyforms").EnumerateArray().ToArray();var morphIds=source.Descendants("KeyFormMorphTarget").Select(t=>Ref(t,"keyformGuid")).ToHashSet();
                var allForms=source.Elements().Single(e=>Name(e)=="keyforms").Elements().ToArray();
                var emitted=allForms.Where(e=>!morphIds.Contains(Ref(e,"guid"))).ToArray();
                Check(allForms.Length==emitted.Length+morphIds.Count,label+": missing or duplicate morph forms.");
                Check(forms.Length==emitted.Length,label+": keyform count differs.");var expected=new List<double[]>();var actual=new List<double[]>();
                for(int f=0;f<Math.Min(forms.Length,emitted.Length);f++)
                {
                    formsCount++;var r=forms[f];var e=emitted[f];var a=new List<double>();var b=new List<double>();
                    if(warp)
                    {
                        var positions=r.GetProperty("positions").EnumerateArray().SelectMany(p=>p.EnumerateArray().Select(N)).ToArray();
                        Check(positions.Length==2L*(d.GetProperty("rows").GetInt32()+1)*(d.GetProperty("columns").GetInt32()+1),label+": warp vertex count differs from divisions.");
                        a.AddRange(positions.Select((v,i)=>canvas?(float)((float)v*(float)scale)+(float)origin[i%2]:v));b.AddRange(Field(e,"positions").Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(D));
                    }
                    else
                    {
                        var o=r.GetProperty("origin").EnumerateArray().Select(N).ToArray();a.AddRange(new[]{N(r.GetProperty("angle_degrees")),canvas?(float)((float)o[0]*(float)scale)+(float)origin[0]:o[0],canvas?(float)((float)o[1]*(float)scale)+(float)origin[1]:o[1],N(r.GetProperty("scale"))*(rotationAncestor||scale==0?1:scale)});
                        b.AddRange(new[]{"angle","originX","originY","scale"}.Select(n=>D((string)e.Attribute(n)!)));
                        Check((string?)e.Attribute("isReflectX")==r.GetProperty("reflected")[0].ToString().ToLowerInvariant()&&(string?)e.Attribute("isReflectY")==r.GetProperty("reflected")[1].ToString().ToLowerInvariant(),label+": reflection flags differ.");
                    }
                    a.Add(N(r.GetProperty("opacity")));b.Add(D(Field(e,"opacity").Value));
                    foreach(var (json,field) in new[]{("multiply_color","multiplyColor"),("screen_color","screenColor")})
                    {a.AddRange(r.GetProperty(json).EnumerateArray().Select(N));var c=Field(e,field);b.AddRange(new[]{"red","green","blue"}.Select(n=>D((string)c.Attribute(n)!)));}
                    string coord=Ref(e,"coordType");Check(Field(objects[coord],"coordName").Value==(canvas?"Canvas":"DeformerLocal"),label+": coordinate space differs.");
                    Check(a.Count==b.Count&&a.Zip(b).All(p=>Same(p.First,p.Second)),label+$": keyform {f} values differ.");expected.Add(a.ToArray());actual.Add(b.ToArray());
                }
                var bi=d.GetProperty("binding_band_index");var axes=bi.ValueKind==JsonValueKind.Null?Array.Empty<JsonElement>():bands[bi.GetInt32()].GetProperty("bindings").EnumerateArray().ToArray();
                var grid=objects[Ref(source,"keyformGridSource")];var bindingRefs=Field(grid,"keyformBindings").Elements().Select(e=>(string)e.Attribute("xs.ref")!).ToArray();
                Check(axes.Length==bindingRefs.Length,label+": binding count differs.");
                var keys=axes.Select(a=>a.GetProperty("keys").EnumerateArray().Select(N).ToArray()).ToArray();long count=1;
                for(int a=0;a<axes.Length;a++){Check(keys[a].Length>0&&keys[a].Zip(keys[a].Skip(1)).All(p=>p.First<p.Second),label+": invalid binding keys.");count=checked(count*keys[a].Length);}
                Check(count==forms.Length,label+": keyform grid size differs.");
                for(int a=0;a<Math.Min(axes.Length,bindingRefs.Length);a++)
                {var binding=objects[bindingRefs[a]];Check(parameterIds[Ref(binding,"parameterGuid")]==pars[axes[a].GetProperty("parameter_index").GetInt32()].GetProperty("id").GetString(),label+": bound parameter differs.");var ek=Field(binding,"keys").Elements().Select(e=>D(e.Value)).ToArray();Check(ek.Length==keys[a].Length&&ek.Zip(keys[a]).All(p=>Same(p.First,p.Second)),label+": binding keys differ.");}
                var gf=grid.Descendants("KeyformOnGrid").ToArray();Check(gf.Length==emitted.Length,label+": grid form count differs.");
                for(int f=0;f<Math.Min(gf.Length,emitted.Length);f++)
                {
                    Check(Ref(gf[f],"keyformGuid")==Ref(emitted[f],"guid"),label+": grid form reference differs.");var access=gf[f].Descendants("KeyOnParameter").ToArray();int stride=1;Check(access.Length==axes.Length,label+": grid axes differ.");
                    for(int a=0;a<Math.Min(access.Length,axes.Length);a++){Check(Ref(access[a],"binding")==bindingRefs[a]&&D(Field(access[a],"keyIndex").Value)==(f/stride)%Math.Max(1,keys[a].Length),label+": grid access key differs.");stride=checked(stride*Math.Max(1,keys[a].Length));}
                }
                if(count==expected.Count&&actual.Count==expected.Count&&actual.Zip(expected).All(p=>p.First.Length==p.Second.Length))
                foreach(var values in SampleValues(axes,pars))
                {
                    var weights=RecoveryValidator.Weights(axes,values);var a=RecoveryValidator.Blend(expected,weights,expected[0].Length);var b=RecoveryValidator.Blend(actual,weights,actual[0].Length);samples++;
                    Check(a.Zip(b).All(p=>Same(p.First,p.Second)),label+": sampled local deformation/opacity/color differs.");
                }
            }
            catch(Exception e) when(e is InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException or InvalidDataException or FormatException or OverflowException or ArgumentException){errors.Add(label+": validation could not complete: "+e.Message);}
        }
        warnings.Add("Multi-axis validation uses pairwise limit corners and 32 deterministic interior combinations per multi-axis object, capped at 2048 samples per object. It is not exhaustive or a world-space simulation.");
        return new(mapped.Count,formsCount,samples);
    }
}
