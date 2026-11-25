using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Globalization;
namespace MocRecovery;
public sealed record Enrichment(int PhysicsSettings,int ParameterGroups,List<string> Notes,List<string> LinkedPairs);
public static class ProjectEnricher
{
    static XAttribute N(string n)=>new("xs.n",n);
    static XElement V(string tag,string name,object value)=>new(tag,N(name),value);
    static XElement G(string tag,string name)=>new(tag,N(name),new XAttribute("uuid",Guid.NewGuid()),new XAttribute("note","Recovered"));
    static XElement Ref(string tag,string name,string id)=>new(tag,N(name),new XAttribute("xs.ref",id));
    static XElement Vec(string name,object x,object y)=>new("GVector2",N(name),V("f","x",x),V("f","y",y));
    static XElement List(string name,IEnumerable<XElement> children){var c=children.ToArray();return new("carray_list",N(name),new XAttribute("count",c.Length),c);}
    static string S(JsonElement j,string key)=>j.GetProperty(key).GetString()??"";
    static string F(JsonElement j,string key){double v=j.GetProperty(key).GetDouble();if(!double.IsFinite(v))throw new InvalidDataException("Non-finite physics value.");return v.ToString("R",CultureInfo.InvariantCulture);}
    static string B(JsonElement j,string key)=>j.GetProperty(key).GetBoolean()?"true":"false";
    static string Type(JsonElement j)=>S(j,"Type") switch{"X"=>"SRC_TO_X","Y"=>"SRC_TO_Y","Angle"=>"SRC_TO_G_ANGLE",_=>throw new InvalidDataException("Unsupported physics axis.")};
    static void Import(XDocument doc,string path){if(!doc.Nodes().OfType<XProcessingInstruction>().Any(p=>p.Target=="import"&&p.Data==path))doc.Root!.AddBeforeSelf(new XProcessingInstruction("import",path));}
    public static Enrichment Apply(string archivePath,Plan plan)
    {
        var entries=CaffArchive.Read(archivePath);var doc=XDocument.Load(new MemoryStream(entries["main.xml"]));
        var model=doc.Descendants("CModelSource").Single(e=>e.HasElements);
        var parameters=model.Descendants("CParameterSource").Where(e=>e.HasElements).ToDictionary(e=>(string)e.Element("CParameterId")!.Attribute("idstr")!,e=>e);
        var parameterGuids=parameters.ToDictionary(k=>k.Key,k=>(string)k.Value.Element("CParameterGuid")!.Attribute("xs.ref")!);
        var sdkTarget=SdkTarget.Apply(doc,plan.Input);
        var notes=new List<string>{"SDK/Cubism compatibility target restored from MOC3: "+sdkTarget+". This does not identify the original Editor build or prove full feature equivalence."};int physics=0,groups=0;
        if(plan.DisplayInfo!=null)
        {
            try
            {
                using var cdi=JsonDocument.Parse(File.ReadAllBytes(plan.DisplayInfo));
                groups=AddGroups(doc,model,parameters,cdi.RootElement);
            }
            catch(Exception e) when(e is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or ArgumentException)
            {notes.Add("PARAMETER GROUPS NOT RESTORED: "+e.Message);}
        }
        var path=plan.PhysicsInfo;
        if(path!=null && File.Exists(path))
        {
            try
            {
                using var file=JsonDocument.Parse(File.ReadAllBytes(path));var json=file.RootElement;
                var settings=new List<XElement>();
                var names=json.GetProperty("Meta").TryGetProperty("PhysicsDictionary",out var dictionary)?dictionary.EnumerateArray().ToDictionary(e=>S(e,"Id"),e=>S(e,"Name")):new Dictionary<string,string>();
                foreach(var setting in json.GetProperty("PhysicsSettings").EnumerateArray())
                {
                    var id=S(setting,"Id");
                    try{settings.Add(Physics(setting,parameterGuids,names.GetValueOrDefault(id,id)));}
                    catch(Exception e) when(e is InvalidDataException or KeyNotFoundException or InvalidOperationException or ArgumentException)
                    {notes.Add($"PHYSICS SETUP {id} NOT RESTORED: {e.Message}");}
                }
                var source=new XElement("CPhysicsSettingsSourceSet",N("physicsSettingsSourceSet"),List("_sourceCubismPhysics",settings));
                if(settings.Count>0)source.Add(new XElement("CPhysicsSettingsGuid",N("selectedCubismPhysics"),new XAttribute("uuid",(string)settings[0].Element("CPhysicsSettingsGuid")!.Attribute("uuid")!)));
                else source.Add(new XElement("null",N("selectedCubismPhysics")));
                source.Add(V("i","settingFPS",json.GetProperty("Meta").TryGetProperty("Fps",out var fps)?fps.GetDouble():30));
                model.Elements("CPhysicsSettingsSourceSet").Remove();model.Add(source);physics=settings.Count;
                foreach(string cls in new[]{"CPhysicsController$CPhysicsSourceType","CPhysicsInput","CPhysicsOutput","CPhysicsSettingsSource","CPhysicsSettingsSourceSet","CPhysicsVertex"})Import(doc,"com.live2d.cubism.doc.gameData.physics."+cls);
                Import(doc,"com.live2d.cubism.doc.model.id.CPhysicsSettingId");Import(doc,"com.live2d.type.CPhysicsDataGuid");Import(doc,"com.live2d.type.CPhysicsSettingsGuid");
                if(json.GetProperty("Meta").TryGetProperty("EffectiveForces",out var forces))
                {
                    var gravity=forces.GetProperty("Gravity");var wind=forces.GetProperty("Wind");
                    if(gravity.GetProperty("X").GetDouble()!=0||gravity.GetProperty("Y").GetDouble()!=-1||wind.GetProperty("X").GetDouble()!=0||wind.GetProperty("Y").GetDouble()!=0)
                        notes.Add("Custom global gravity/wind is preserved in physics3.json but is not mapped to Editor settings.");
                }
            }
            catch(Exception e) when(e is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException or ArgumentException)
            {notes.Add("PHYSICS IMPORT INCOMPLETE: "+e.Message);}
        }
        var links=plan.InferParameterLinks ? InferLinks(doc,parameters,entries["moc2cmo.model.json"]) : new List<string>();
        if(plan.InferParameterLinks)notes.Add("Linked parameter controls are inferred from adjacent parameters sharing a two-axis keyform grid. Original Editor linking choices are not stored in MOC3; additional or missing links are possible.");
        Validate(doc);
        using var ms=new MemoryStream();
        using(var writer=System.Xml.XmlWriter.Create(ms,new System.Xml.XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=true,CloseOutput=false}))doc.Save(writer);
        entries["main.xml"]=ms.ToArray();
        CaffArchive.Write(archivePath,entries);
        return new(physics,groups,notes,links);
    }
    static XElement Physics(JsonElement j,Dictionary<string,string> guids,string name)
    {
        var vertices=j.GetProperty("Vertices").EnumerateArray().Select(v=>new XElement("CPhysicsVertex",G("CPhysicsDataGuid","guid"),Vec("position",F(v.GetProperty("Position"),"X"),F(v.GetProperty("Position"),"Y")),V("f","mobility",F(v,"Mobility")),V("f","delay",F(v,"Delay")),V("f","acceleration",F(v,"Acceleration")),V("f","radius",F(v,"Radius")))).ToArray();
        string Parameter(JsonElement reference){if(S(reference,"Target")!="Parameter")throw new InvalidDataException("Physics target is not a parameter.");string id=S(reference,"Id");return guids.TryGetValue(id,out var guid)?guid:throw new InvalidDataException("Parameter absent from MOC3: "+id);}
        var inputs=j.GetProperty("Input").EnumerateArray().Select(i=>new XElement("CPhysicsInput",G("CPhysicsDataGuid","guid"),Ref("CParameterGuid","source",Parameter(i.GetProperty("Source"))),V("f","angleScale",0),Vec("translationScale",0,0),V("f","weight",F(i,"Weight")),new XElement("CPhysicsSourceType",N("type"),new XAttribute("v",Type(i))),V("b","isReverse",B(i,"Reflect")))).ToArray();
        var outputs=j.GetProperty("Output").EnumerateArray().Select(o=>{
            int vertex=o.GetProperty("VertexIndex").GetInt32();if(vertex<1||vertex>=vertices.Length)throw new InvalidDataException("Output vertex index outside particle chain.");string type=S(o,"Type"),scale=F(o,"Scale");
            return new XElement("CPhysicsOutput",G("CPhysicsDataGuid","guid"),Ref("CParameterGuid","destination",Parameter(o.GetProperty("Destination"))),V("i","vertexIndex",vertex),Vec("translationScale",type=="X"?scale:"0",type=="Y"?scale:"0"),V("f","angleScale",type=="Angle"?scale:"0"),V("f","weight",F(o,"Weight")),new XElement("CPhysicsSourceType",N("type"),new XAttribute("v",Type(o))),V("b","isReverse",B(o,"Reflect")));
        }).ToArray();
        var x=new XElement("CPhysicsSettingsSource",V("s","name",name),G("CPhysicsSettingsGuid","guid"),new XElement("CPhysicsSettingId",N("id"),new XAttribute("idstr",S(j,"Id"))),List("inputs",inputs),List("outputs",outputs),List("vertices",vertices));
        var norm=j.GetProperty("Normalization");
        foreach(string axis in new[]{"Position","Angle"})
        {var a=norm.GetProperty(axis);x.Add(V("f","normalized"+axis+"ValueMax",F(a,"Maximum")),V("f","normalized"+axis+"ValueMin",F(a,"Minimum")),V("f","normalized"+axis+"DefaultValue",F(a,"Default")));}
        return x;
    }
    static int AddGroups(XDocument doc,XElement model,Dictionary<string,XElement> parameters,JsonElement cdi)
    {
        if(!cdi.TryGetProperty("ParameterGroups",out var groupArray))return 0;
        var groups=groupArray.EnumerateArray().ToDictionary(g=>S(g,"Id"),g=>g);
        if(groups.ContainsKey(""))throw new InvalidDataException("Empty group identifier.");
        foreach(var (id,g) in groups)
        {var seen=new HashSet<string>{id};string parent=S(g,"GroupId");while(parent!=""){if(!seen.Add(parent)||!groups.ContainsKey(parent))throw new InvalidDataException("Unknown parent or cycle in parameter groups.");parent=S(groups[parent],"GroupId");}}
        var order=cdi.GetProperty("Parameters").EnumerateArray().ToArray();
        foreach(var p in order){var parent=S(p,"GroupId");if(parent!=""&&!groups.ContainsKey(parent))throw new InvalidDataException("Unknown parameter group: "+parent);}
        var shared=doc.Root!.Element("shared")!;int next=doc.Descendants().Attributes("xs.id").Select(a=>int.Parse(a.Value.TrimStart('#'))).DefaultIfEmpty().Max()+1;
        string root=parameters.Values.SelectMany(p=>p.Elements("CParameterGroupGuid")).Select(g=>(string?)g.Attribute("xs.ref")).First(g=>g!=null)!;
        var ids=new Dictionary<string,string>{{"",root}};var newGuids=new List<XElement>();
        foreach(string id in groups.Keys){string g="#"+next++;ids[id]=g;newGuids.Add(new XElement("CParameterGroupGuid",new XAttribute("xs.id",g),new XAttribute("uuid",Guid.NewGuid()),new XAttribute("note",id)));}
        var children=ids.Keys.ToDictionary(k=>k,k=>new List<XElement>());
        foreach(var (id,g) in groups)children[S(g,"GroupId")].Add(new XElement("CParameterGroupGuid",new XAttribute("xs.ref",ids[id])));
        var parents=new Dictionary<string,string>();
        foreach(var p in order)if(parameters.ContainsKey(S(p,"Id")))parents.Add(S(p,"Id"),S(p,"GroupId"));
        foreach(var id in parameters.Keys)parents.TryAdd(id,"");
        foreach(var (id,parent) in parents)children[parent].Add(new XElement("CParameterGuid",new XAttribute("xs.ref",(string)parameters[id].Element("CParameterGuid")!.Attribute("xs.ref")!)));
        var sources=new List<XElement>();
        foreach(var (id,guid) in ids)
        {
            var x=new XElement("CParameterGroup",V("s","name",id==""?"Root Parameter Group":S(groups[id],"Name")),V("s","description",""),V("b","folderIsOpened","true"),Ref("CParameterGroupGuid","guid",guid),id==""?new XElement("null",N("parentGroupGuid")):Ref("CParameterGroupGuid","parentGroupGuid",ids[S(groups[id],"GroupId")]),List("_childGuids",children[id]),new XElement("CParameterGroupId",N("id"),new XAttribute("idstr",id==""?"ParamGroupRoot":id)));
            foreach(string color in new[]{"Red","Green","Blue","Alpha"})x.Add(V("f","visibilityColor"+color,1));sources.Add(x);
        }
        // Commit only after the complete hierarchy has been checked.
        shared.Add(newGuids);
        foreach(var (id,parent) in parents)parameters[id].Elements("CParameterGroupGuid").Single(e=>(string?)e.Attribute("xs.n")=="parentGroupGuid").SetAttributeValue("xs.ref",ids[parent]);
        var set=model.Element("CParameterGroupSet")!;set.ReplaceNodes(List("_groups",sources));
        Import(doc,"com.live2d.cubism.doc.model.param.group.CParameterGroup");Import(doc,"com.live2d.cubism.doc.model.id.CParameterGroupId");
        if(!doc.Nodes().OfType<XProcessingInstruction>().Any(p=>p.Target=="version"&&p.Data.StartsWith("CParameterGroup:")))doc.Root!.AddBeforeSelf(new XProcessingInstruction("version","CParameterGroup:3"));
        return groups.Count;
    }
    static List<string> InferLinks(XDocument doc,Dictionary<string,XElement> parameters,byte[] runtime)
    {
        using var data=JsonDocument.Parse(runtime);
        var paramIds=data.RootElement.GetProperty("parameters").EnumerateArray().Select(p=>S(p,"id")).ToArray();
        var candidates=new HashSet<(string,string)>();
        (string,string) Pair(string a,string b)=>string.CompareOrdinal(a,b)<0?(a,b):(b,a);
        foreach(var band in data.RootElement.GetProperty("binding_bands").EnumerateArray())
        {
            var axes=band.GetProperty("bindings").EnumerateArray().ToArray();
            if(axes.Length!=2||axes.Any(a=>a.GetProperty("keys").GetArrayLength()<2))continue;
            int a=axes[0].GetProperty("parameter_index").GetInt32(),b=axes[1].GetProperty("parameter_index").GetInt32();
            if(a<0||b<0||a>=paramIds.Length||b>=paramIds.Length||a==b)continue;
            candidates.Add(Pair(paramIds[a],paramIds[b]));
        }
        var byGuid=parameters.ToDictionary(p=>(string)p.Value.Element("CParameterGuid")!.Attribute("xs.ref")!,p=>p.Key);
        var sequences=new List<string?[]>();
        foreach(var g in doc.Descendants("CParameterGroup").Where(g=>g.HasElements))
        {
            var children=g.Elements("carray_list").FirstOrDefault(e=>(string?)e.Attribute("xs.n")=="_childGuids");
            if(children!=null)sequences.Add(children.Elements().Select(e=>e.Name=="CParameterGuid"?byGuid.GetValueOrDefault((string?)e.Attribute("xs.ref")??""):null).ToArray());
        }
        if(sequences.Count==0)sequences.Add(parameters.Keys.Cast<string?>().ToArray());
        foreach(var p in parameters.Values)
        {
            var flag=p.Elements("b").SingleOrDefault(e=>(string?)e.Attribute("xs.n")=="combined");
            if(flag==null)p.Add(V("b","combined","false"));else flag.Value="false";
        }
        var linked=new List<string>();var used=new HashSet<string>();
        foreach(var sequence in sequences)
        for(int i=0;i+1<sequence.Length;i++)
        {
            string? a=sequence[i],b=sequence[i+1];
            if(a==null||b==null||used.Contains(a)||used.Contains(b)||!candidates.Contains(Pair(a,b)))continue;
            parameters[a].Elements("b").Single(e=>(string?)e.Attribute("xs.n")=="combined").Value="true";
            used.Add(a);used.Add(b);
            string Label(string id)=>parameters[id].Elements("s").FirstOrDefault(e=>(string?)e.Attribute("xs.n")=="name")?.Value??id;
            linked.Add($"{Label(a)} [{a}] + {Label(b)} [{b}]");i++;
        }
        return linked;
    }
    public static void Validate(XDocument doc)
    {
        var idList=doc.Descendants().Attributes("xs.id").Select(a=>a.Value).ToArray();var ids=idList.ToHashSet();if(ids.Count!=idList.Length)throw new InvalidDataException("Duplicate XML object identifiers.");
        var missing=doc.Descendants().Attributes("xs.ref").Select(a=>a.Value).Where(r=>!ids.Contains(r)).Distinct().ToArray();if(missing.Length>0)throw new InvalidDataException("Unresolved XML references: "+string.Join(", ",missing));
    }
}
