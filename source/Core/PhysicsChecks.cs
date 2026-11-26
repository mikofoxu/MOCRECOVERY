using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
namespace MocRecovery;
public static class PhysicsChecks
{
    public static int Compare(XDocument xml,string? path,List<string> errors,List<string> warnings)
    {
        if(path==null||!File.Exists(path))return 0;
        int checkedCount=0;
        try
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(path));var original=doc.RootElement;
            var model=xml.Root?.Element("main")?.Element("CModelSource");
            var settings=model?.Elements("CPhysicsSettingsSourceSet").SingleOrDefault(e=>(string?)e.Attribute("xs.n")=="physicsSettingsSourceSet");
            var list=settings?.Elements("carray_list").SingleOrDefault(e=>(string?)e.Attribute("xs.n")=="_sourceCubismPhysics");
            var sources=(list?.Elements("CPhysicsSettingsSource")??Enumerable.Empty<XElement>()).ToDictionary(e=>(string)e.Element("CPhysicsSettingId")!.Attribute("idstr")!);
            if(sources.Count>0)
            {
                var selected=(string?)settings!.Element("CPhysicsSettingsGuid")?.Attribute("xs.ref");
                if((int?)list!.Attribute("count")!=sources.Count || selected==null ||
                    !xml.Root!.Element("shared")!.Elements("CPhysicsSettingsGuid").Any(g=>(string?)g.Attribute("xs.id")==selected &&
                        Guid.TryParse((string?)g.Attribute("uuid"),out _) && g.Attribute("note")!=null &&
                        int.TryParse((string?)g.Attribute("xs.idx"),out int index) && index>=0) ||
                    sources.Values.Count(e=>(string?)e.Element("CPhysicsSettingsGuid")?.Attribute("xs.ref")==selected)!=1)
                    errors.Add("Invalid physics group list or selected group reference.");
            }
            else if(xml.Descendants("CPhysicsSettingsSource").Any(e=>e.HasElements))
                errors.Add("Physics groups are outside the model's physics group list.");
            var parameters=PhysicsModelMetadata.ParameterIds(xml);
            XElement Field(XElement e,string n)=>e.Descendants().Single(x=>(string?)x.Attribute("xs.n")==n);
            void Equal(double a,double b,string label){if(!double.IsFinite(a)||!double.IsFinite(b)||Math.Abs(a-b)>Math.Max(0.00002,Math.Abs(a)*0.000002))errors.Add(label+": physics value differs from companion.");}
            double N(JsonElement j)=>CompanionExporter.Number(j);
            void Value(XElement e,string field,JsonElement j,string key,string label)=>Equal(double.Parse(Field(e,field).Value,CultureInfo.InvariantCulture),N(j.GetProperty(key)),label+"/"+key);
            foreach(var setting in original.GetProperty("PhysicsSettings").EnumerateArray())
            {
                string id=setting.GetProperty("Id").GetString()!;
                if(!sources.TryGetValue(id,out var source)){warnings.Add(id+": companion physics setup was not imported; see import notes.");continue;}
                checkedCount++;
                foreach(var (json,tag,reference) in new[]{("Input","CPhysicsInput","Source"),("Output","CPhysicsOutput","Destination")})
                {
                    var a=setting.GetProperty(json).EnumerateArray().ToArray();var b=source.Descendants(tag).ToArray();
                    if(a.Length!=b.Length)errors.Add(id+": physics "+json+" count differs.");
                    for(int i=0;i<Math.Min(a.Length,b.Length);i++)
                    {
                        var r=a[i];var e=b[i];string label=id+"/"+json+"/"+i;
                        string pg=(string)e.Element("CParameterGuid")!.Attribute("xs.ref")!;
                        if(parameters.GetValueOrDefault(pg)!=r.GetProperty(reference).GetProperty("Id").GetString())errors.Add(label+": physics parameter differs from companion.");
                        Value(e,"weight",r,"Weight",label);
                        string type=r.GetProperty("Type").GetString()!;string expected=type switch{"X"=>"SRC_TO_X","Y"=>"SRC_TO_Y","Angle"=>"SRC_TO_G_ANGLE",_=>"INVALID"};
                        if((string?)e.Element("CPhysicsSourceType")?.Attribute("v")!=expected||Field(e,"isReverse").Value!=r.GetProperty("Reflect").ToString().ToLowerInvariant())errors.Add(label+": physics type/reflection differs from companion.");
                        if(json=="Output")
                        {Value(e,"vertexIndex",r,"VertexIndex",label);double scale=type=="Angle"?double.Parse(Field(e,"angleScale").Value,CultureInfo.InvariantCulture):double.Parse(Field(Field(e,"translationScale"),type=="X"?"x":"y").Value,CultureInfo.InvariantCulture);Equal(scale,N(r.GetProperty("Scale")),label+"/Scale");}
                    }
                }
                var vertices=setting.GetProperty("Vertices").EnumerateArray().ToArray();var actual=source.Descendants("CPhysicsVertex").ToArray();if(vertices.Length!=actual.Length)errors.Add(id+": physics vertex count differs.");
                for(int i=0;i<Math.Min(vertices.Length,actual.Length);i++)
                {
                    var v=vertices[i];var e=actual[i];foreach(string key in new[]{"Mobility","Delay","Acceleration","Radius"})Value(e,char.ToLowerInvariant(key[0])+key[1..],v,key,id+"/vertex/"+i);
                    foreach(string axis in new[]{"X","Y"})Equal(double.Parse(Field(Field(e,"position"),axis.ToLowerInvariant()).Value,CultureInfo.InvariantCulture),N(v.GetProperty("Position").GetProperty(axis)),id+"/position/"+axis);
                }
                foreach(string axis in new[]{"Position","Angle"})foreach(var (key,suffix) in new[]{("Minimum","ValueMin"),("Maximum","ValueMax"),("Default","DefaultValue")})Value(source,"normalized"+axis+suffix,setting.GetProperty("Normalization").GetProperty(axis),key,id+"/"+axis);
            }
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException or IOException)
        {if(checkedCount>0)errors.Add("Physics comparison could not complete: "+e.Message);else warnings.Add("Physics companion comparison unavailable: "+e.Message);}
        return checkedCount;
    }
}
