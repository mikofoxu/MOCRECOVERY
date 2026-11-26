using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
namespace MocRecovery;
public sealed record ValidationResult(int Meshes,int Keyforms,int Samples,List<string> Errors,List<string> Warnings)
{ public RigCheckResult? Rig {get;init;} public int PhysicsSetupsCompared {get;init;} }
public static class RecoveryValidator
{
    static string? Name(XElement e)=>(string?)e.Attribute("xs.n");
    static double N(JsonElement e)=>CompanionExporter.Number(e);
    static double[] Array(XElement e)=>e.Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();
    static bool Same(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=Math.Max(0.00002,Math.Max(Math.Abs(a),Math.Abs(b))*0.000002);
    public static ValidationResult Validate(string archivePath,string destination,string? physicsPath=null)
    {
        var archive=CaffArchive.Read(archivePath);using var runtime=JsonDocument.Parse(archive["moc2cmo.model.json"]);var model=runtime.RootElement;
        var xml=XDocument.Load(new MemoryStream(archive["main.xml"]));ProjectEnricher.Validate(xml);
        var errors=new List<string>();var warnings=new List<string>();int formsChecked=0,samples=0;
        var meshes=model.GetProperty("art_meshes").EnumerateArray().ToArray();var bands=model.GetProperty("binding_bands").EnumerateArray().ToArray();
        var parameters=model.GetProperty("parameters").EnumerateArray().ToArray();
        var objects=xml.Descendants().Where(e=>e.Attribute("xs.id")!=null).ToDictionary(e=>(string)e.Attribute("xs.id")!);
        var parameterGuids=PhysicsModelMetadata.ParameterIds(xml);
        var sources=xml.Descendants("CArtMeshSource").Where(e=>e.HasElements).ToDictionary(e=>(string)e.Descendants("CDrawableId").Single().Attribute("idstr")!);
        var guids=sources.ToDictionary(p=>(string)p.Value.Descendants("CDrawableGuid").First(e=>Name(e)=="guid").Attribute("xs.ref")!,p=>p.Key);
        var canvas=model.GetProperty("canvas");double scale=N(canvas.GetProperty("pixels_per_unit"));var origin=canvas.GetProperty("origin").EnumerateArray().Select(N).ToArray();
        void Check(bool ok,string message){if(!ok)errors.Add(message);}
        foreach(var mesh in meshes)
        {
            string id=mesh.GetProperty("id").GetString()!;
            try
            {
                if(!sources.TryGetValue(id,out var source)){errors.Add(id+": missing editor mesh.");continue;}
                var indices=mesh.GetProperty("triangle_indices").EnumerateArray().Select(e=>e.GetInt32()).ToArray();
                var uv=mesh.GetProperty("uvs").EnumerateArray().SelectMany(e=>e.EnumerateArray().Select(N)).ToArray();
                Check(indices.Length%3==0&&indices.All(i=>i>=0&&i<uv.Length/2),id+": invalid triangle indices.");
                Check(Array(source.Elements("int-array").Single(e=>Name(e)=="indices")).SequenceEqual(indices.Select(i=>(double)i)),id+": editor triangle indices differ.");
                var editorUv=Array(source.Elements("float-array").Single(e=>Name(e)=="uvs"));
                Check(editorUv.Length==uv.Length&&editorUv.Zip(uv).All(p=>Same(p.First,p.Second)),id+": editor UVs differ.");
                var masks=mesh.GetProperty("mask_drawable_indices").EnumerateArray().Select(e=>e.GetInt32()).ToArray();
                Check(masks.All(i=>i>=0&&i<meshes.Length),id+": clipping target outside mesh table.");
                var expectedMasks=masks.Where(i=>i>=0&&i<meshes.Length).Select(i=>meshes[i].GetProperty("id").GetString()!).ToArray();
                var actualMasks=source.Descendants("carray_list").Single(e=>Name(e)=="clipGuidList").Elements().Select(e=>guids.GetValueOrDefault((string?)e.Attribute("xs.ref")??"","MISSING")).ToArray();
                Check(expectedMasks.SequenceEqual(actualMasks),id+": editor clipping masks differ.");
                int flags=mesh.GetProperty("drawable_flags").GetInt32();
                Check(source.Descendants("b").Single(e=>Name(e)=="invertClippingMask").Value==((flags&8)!=0?"true":"false"),id+": inverted clipping flag differs.");
                Check(source.Elements("b").Single(e=>Name(e)=="culling").Value==((flags&4)==0?"true":"false"),id+": culling flag differs.");
                var forms=mesh.GetProperty("keyforms").EnumerateArray().ToArray();var morphIds=source.Descendants("KeyFormMorphTarget").Select(e=>(string)e.Element("CFormGuid")!.Attribute("xs.ref")!).ToHashSet();
                var allForms=source.Elements().Single(e=>Name(e)=="keyforms").Elements("CArtMeshForm").ToArray();
                var editorForms=allForms.Where(e=>!morphIds.Contains((string)e.Descendants("CFormGuid").Single().Attribute("xs.ref")!)).ToArray();
                Check(allForms.Length==editorForms.Length+morphIds.Count,id+": missing or duplicate mesh morph forms.");
                Check(forms.Length==editorForms.Length,id+": keyform count differs.");
                var pointSets=new List<double[]>();var editorSets=new List<double[]>();bool isCanvas=mesh.GetProperty("parent_deformer_index").ValueKind==JsonValueKind.Null;
                for(int f=0;f<Math.Min(forms.Length,editorForms.Length);f++)
                {
                    formsChecked++;
                    var points=forms[f].GetProperty("positions").EnumerateArray().SelectMany(e=>e.EnumerateArray().Select(N)).ToArray();
                    Check(points.Length==uv.Length,id+": keyform vertex count differs from UVs.");
                    if(isCanvas)for(int n=0;n<points.Length;n++)points[n]=points[n]*scale+origin[n%2];
                    var ep=Array(editorForms[f].Elements("float-array").Single(e=>Name(e)=="positions"));
                    Check(ep.Length==points.Length&&ep.Zip(points).All(p=>Same(p.First,p.Second)),id+$": keyform {f} positions differ.");
                    var opacity=double.Parse(editorForms[f].Descendants("f").Single(e=>Name(e)=="opacity").Value,CultureInfo.InvariantCulture);
                    Check(Same(opacity,N(forms[f].GetProperty("opacity"))),id+$": keyform {f} opacity differs.");
                    foreach(var (field,editorField) in new[]{("multiply_color","multiplyColor"),("screen_color","screenColor")})
                    {
                        var color=editorForms[f].Descendants("CFloatColor").Single(e=>Name(e)==editorField);
                        var actual=new[]{"red","green","blue"}.Select(n=>double.Parse((string)color.Attribute(n)!,CultureInfo.InvariantCulture)).ToArray();
                        var expectedColor=forms[f].GetProperty(field).EnumerateArray().Select(N).ToArray();
                        Check(actual.Length==expectedColor.Length&&actual.Zip(expectedColor).All(p=>Same(p.First,p.Second)),id+$": keyform {f} {field} differs.");
                    }
                    double order=N(forms[f].GetProperty("draw_order"));
                    double editorOrder=double.Parse(editorForms[f].Descendants("i").Single(e=>Name(e)=="drawOrder").Value,CultureInfo.InvariantCulture);
                    Check(editorOrder==Math.Round(order,MidpointRounding.AwayFromZero),id+$": keyform {f} draw order differs.");
                    if(Math.Abs(order-editorOrder)>0.00001)warnings.Add(id+$": fractional draw order {order} rounded to {editorOrder}. This may change ordering at ties.");
                    pointSets.Add(points);editorSets.Add(ep);
                }
                var bi=mesh.GetProperty("binding_band_index");var axes=bi.ValueKind==JsonValueKind.Null?System.Array.Empty<JsonElement>():bands[bi.GetInt32()].GetProperty("bindings").EnumerateArray().ToArray();
                long expected=1;
                foreach(var axis in axes)
                {
                    var keys=axis.GetProperty("keys").EnumerateArray().Select(N).ToArray();int pi=axis.GetProperty("parameter_index").GetInt32();
                    Check(pi>=0&&pi<parameters.Length,id+": invalid bound parameter.");
                    Check(keys.Length>0&&keys.Zip(keys.Skip(1)).All(p=>p.First<p.Second),id+": parameter keys are not strictly increasing.");expected=checked(expected*Math.Max(1,keys.Length));
                }
                Check(expected==forms.Length,id+": keyform grid size differs from axis product.");
                var gridRef=source.Descendants("KeyformGridSource").Single(e=>e.Attribute("xs.ref")!=null);
                var grid=objects[(string)gridRef.Attribute("xs.ref")!];
                var bindings=grid.Elements("array_list").Single(e=>Name(e)=="keyformBindings").Elements().Select(e=>objects[(string)e.Attribute("xs.ref")!]).ToArray();
                Check(bindings.Length==axes.Length,id+": emitted binding axis count differs.");
                for(int axis=0;axis<Math.Min(bindings.Length,axes.Length);axis++)
                {
                    string pg=(string)bindings[axis].Element("CParameterGuid")!.Attribute("xs.ref")!;
                    Check(parameterGuids[pg]==parameters[axes[axis].GetProperty("parameter_index").GetInt32()].GetProperty("id").GetString(),id+": emitted binding parameter differs.");
                    var ek=bindings[axis].Elements("array_list").Single(e=>Name(e)=="keys").Elements().Select(e=>double.Parse(e.Value,CultureInfo.InvariantCulture)).ToArray();
                    var rk=axes[axis].GetProperty("keys").EnumerateArray().Select(N).ToArray();
                    Check(ek.Length==rk.Length&&ek.Zip(rk).All(p=>Same(p.First,p.Second)),id+": emitted binding keys differ.");
                }
                var gridForms=grid.Descendants("KeyformOnGrid").ToArray();
                Check(gridForms.Length==editorForms.Length,id+": emitted grid form count differs.");
                for(int f=0;f<Math.Min(gridForms.Length,editorForms.Length);f++)
                {
                    Check((string?)gridForms[f].Element("CFormGuid")?.Attribute("xs.ref")== (string?)editorForms[f].Descendants("CFormGuid").Single().Attribute("xs.ref"),id+": keyform grid mapping differs.");
                    var keys=gridForms[f].Descendants("KeyOnParameter").ToArray();int stride=1;
                    Check(keys.Length==axes.Length,id+": grid key axis count differs.");
                    for(int a=0;a<Math.Min(keys.Length,axes.Length);a++)
                    {int count=axes[a].GetProperty("keys").GetArrayLength();Check(int.Parse(keys[a].Element("i")!.Value)==(f/stride)%Math.Max(count,1),id+": grid key index differs.");stride=checked(stride*Math.Max(count,1));}
                }

                // Independent tensor interpolation of decoded MOC data and emitted editor keyforms.
                // Sample each bound axis at limits, default, keys and interval midpoints with other axes at defaults.
                if(expected==forms.Length&&pointSets.Count==forms.Length&&editorSets.All(p=>p.Length==uv.Length))
                {
                    var tests=RigChecks.SampleValues(axes,parameters);
                    bool geometryWarning=false;
                    foreach(var values in tests)
                    {
                        var weights=Weights(axes,values);var a=Blend(pointSets,weights,uv.Length);var b=Blend(editorSets,weights,uv.Length);samples++;
                        Check(a.Zip(b).All(p=>Same(p.First,p.Second)),id+": sampled local interpolation differs.");
                        double Expected(string field)=>weights.Sum(w=>N(forms[w.Index].GetProperty(field))*w.Weight);
                        double Actual(string field)=>weights.Sum(w=>double.Parse(editorForms[w.Index].Descendants().Single(e=>Name(e)==field).Value,CultureInfo.InvariantCulture)*w.Weight);
                        Check(Same(Expected("opacity"),Actual("opacity")),id+": sampled opacity differs.");
                        if(Math.Abs(Expected("draw_order")-Actual("drawOrder"))>0.50002)errors.Add(id+": sampled draw order differs.");

                        if(!geometryWarning&&indices.All(i=>i>=0&&i*2+1<a.Length))
                        for(int i=0;i+2<indices.Length;i+=3)
                        {
                            double Area(double[] p){int x=indices[i]*2,y=indices[i+1]*2,z=indices[i+2]*2;return (p[y]-p[x])*(p[z+1]-p[x+1])-(p[y+1]-p[x+1])*(p[z]-p[x]);}
                            if(Area(a)*Area(pointSets[0])<0){warnings.Add(id+": sampled triangle reverses winding relative to first keyform; may be intentional. Review deformation/culling in Editor.");geometryWarning=true;break;}
                        }
                    }
                }
            }
            catch(Exception e) when(e is InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException or InvalidDataException or FormatException or OverflowException or ArgumentException)
            {errors.Add(id+": validation could not complete: "+e.Message);}
        }
        var rig=RigChecks.Validate(model,xml,errors,warnings);
        // Physics data is checked for finite values and valid particle references. No dynamics equivalence claim.
        foreach(var setup in xml.Descendants("CPhysicsSettingsSource").Where(e=>e.HasElements))
        {
            string label=setup.Elements("s").FirstOrDefault(e=>Name(e)=="name")?.Value??"physics";
            foreach(var field in setup.Descendants("f"))Check(double.TryParse(field.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&double.IsFinite(v),label+": non-finite physics field.");
            foreach(var io in setup.Descendants().Where(e=>e.Name=="CPhysicsInput"||e.Name=="CPhysicsOutput"))
            {
                var pg=(string?)io.Element("CParameterGuid")?.Attribute("xs.ref");
                Check(pg!=null&&parameterGuids.ContainsKey(pg),label+": physics parameter reference is invalid.");
                var weight=double.Parse(io.Elements("f").Single(e=>Name(e)=="weight").Value,CultureInfo.InvariantCulture);
                Check(weight>=0&&weight<=100,label+": physics weight is outside 0..100.");
                Check(new[]{"SRC_TO_X","SRC_TO_Y","SRC_TO_G_ANGLE"}.Contains((string?)io.Element("CPhysicsSourceType")?.Attribute("v")),label+": unknown physics source type.");
            }
            int vertices=setup.Descendants("CPhysicsVertex").Count();
            foreach(var output in setup.Descendants("CPhysicsOutput"))
            {int i=int.Parse(output.Elements("i").Single(e=>Name(e)=="vertexIndex").Value,CultureInfo.InvariantCulture);Check(i>0&&i<vertices,label+": invalid physics output particle.");}
            foreach(var axis in new[]{"Position","Angle"})
            {
                double F(string suffix)=>double.Parse(setup.Elements("f").Single(e=>Name(e)=="normalized"+axis+suffix).Value,CultureInfo.InvariantCulture);
                Check(F("ValueMin")<=F("DefaultValue")&&F("DefaultValue")<=F("ValueMax"),label+": invalid physics normalization range.");
            }
        }
        int physicsCompared=PhysicsChecks.Compare(xml,physicsPath,errors,warnings);
        Directory.CreateDirectory(destination);
        var result=new ValidationResult(meshes.Length,formsChecked,samples,errors,warnings.Distinct().ToList()){Rig=rig,PhysicsSetupsCompared=physicsCompared};
        File.WriteAllText(Path.Combine(destination,"validation.json"),JsonSerializer.Serialize(new{result,scope="Mesh keyforms and sampled local tensor interpolation, UVs, triangles, clipping IDs/flags, culling, draw order, opacity, physics numeric/range/particle/parameter checks, warp and rotation keyforms, hierarchy and binding grids, bounded simultaneous parameter samples",notVerified=new[]{"World-space deformer evaluation and interpolation equivalence to Cubism Core","Rendered clipping/compositing","Physics simulation over time","All multi-parameter combinations"}},new JsonSerializerOptions{WriteIndented=true}));
        WriteSweep(parameters,Path.Combine(destination,"parameter-sweep.motion3.json"));
        WritePairSweep(parameters,bands,Path.Combine(destination,"parameter-pairs.motion3.json"));
        return result;
    }
    internal static List<(int Index,double Weight)> Weights(JsonElement[] axes,double[] values)
    {
        var r=new List<(int,double)>{(0,1)};int stride=1;
        for(int a=0;a<axes.Length;a++)
        {
            var keys=axes[a].GetProperty("keys").EnumerateArray().Select(N).ToArray();int lower=0;double f=0;
            while(lower+1<keys.Length&&values[a]>=keys[lower+1])lower++;
            if(lower+1<keys.Length)f=Math.Clamp((values[a]-keys[lower])/(keys[lower+1]-keys[lower]),0,1);
            var next=new List<(int,double)>();foreach(var (i,w) in r){next.Add((i+lower*stride,w*(1-f)));if(f>0)next.Add((i+(lower+1)*stride,w*f));}r=next;stride=checked(stride*keys.Length);
        }
        return r;
    }
    internal static double[] Blend(List<double[]> forms,List<(int Index,double Weight)> weights,int size)
    {var r=new double[size];foreach(var (i,w) in weights)for(int k=0;k<size;k++)r[k]+=forms[i][k]*w;return r;}
    static void WritePairSweep(JsonElement[] parameters,JsonElement[] bands,string path)
    {
        var pairs=new HashSet<(int,int)>();
        foreach(var band in bands)
        {
            var axes=band.GetProperty("bindings").EnumerateArray().Select(a=>a.GetProperty("parameter_index").GetInt32()).Distinct().Order().ToArray();
            for(int a=0;a<axes.Length;a++)for(int b=a+1;b<axes.Length;b++)if(pairs.Count<64)pairs.Add((axes[a],axes[b]));
        }
        if(pairs.Count==0)return;
        var ordered=pairs.OrderBy(p=>p.Item1).ThenBy(p=>p.Item2).ToArray();
        var curves=new List<(string,string,List<(double,double)>)>();
        for(int p=0;p<parameters.Length;p++)
        {
            double d=N(parameters[p].GetProperty("default")),min=N(parameters[p].GetProperty("minimum")),max=N(parameters[p].GetProperty("maximum"));
            var points=new SortedDictionary<double,double>{{0,d}};
            for(int i=0;i<ordered.Length;i++)
            {
                var (a,b)=ordered[i];double t=i*6;points[t]=d;
                points[t+1]=p==a||p==b?min:d;points[t+2]=p==a?max:p==b?min:d;
                points[t+3]=p==a||p==b?max:d;points[t+4]=p==a?min:p==b?max:d;points[t+5]=d;points[t+6]=d;
            }
            curves.Add(("Parameter",parameters[p].GetProperty("id").GetString()!,points.Select(v=>(v.Key,v.Value)).ToList()));
        }
        CompanionPreviews.Write(path,ordered.Length*6,curves);
        File.WriteAllText(Path.ChangeExtension(path,"schedule.json"),JsonSerializer.Serialize(ordered.Select((p,i)=>new{startSeconds=i*6,endSeconds=(i+1)*6,first=parameters[p.Item1].GetProperty("id").GetString(),second=parameters[p.Item2].GetProperty("id").GetString(),sequence="defaults, min/min, max/min, max/max, min/max, defaults",limit="First 64 distinct co-bound parameter pairs; defaults for other parameters"}),new JsonSerializerOptions{WriteIndented=true}));
    }
    static void WriteSweep(JsonElement[] parameters,string path)
    {
        var curves=new List<object>();int totalSegments=0,totalPoints=0;
        for(int i=0;i<parameters.Length;i++)
        {
            var p=parameters[i];double d=N(p.GetProperty("default")),min=N(p.GetProperty("minimum")),max=N(p.GetProperty("maximum"));var segments=new List<double>{0,d};
            var points=new List<(double,double)>();if(i>0)points.Add((i*4,d));
            points.Add((i*4+1,min));points.Add((i*4+2,max));points.Add((i*4+3,d));points.Add((parameters.Length*4,d));
            foreach(var (t,v) in points)segments.AddRange(new[]{0.0,t,v});totalSegments+=points.Count;totalPoints+=points.Count+1;
            curves.Add(new{Target="Parameter",Id=p.GetProperty("id").GetString(),Segments=segments});
        }
        File.WriteAllText(path,JsonSerializer.Serialize(new{Version=3,Meta=new{Duration=parameters.Length*4,Fps=30,Loop=false,AreBeziersRestricted=true,CurveCount=curves.Count,TotalSegmentCount=totalSegments,TotalPointCount=totalPoints,UserDataCount=0,TotalUserDataSize=0},Curves=curves,UserData=System.Array.Empty<object>()},new JsonSerializerOptions{WriteIndented=true}));
    }
}
