using System.Buffers.Binary;
using System.Text.Json;
using System.Xml.Linq;
using MocRecovery;

// Independent synthetic runtime tables. Both objects have two ordinary forms;
// the second is closest to the default. The warp has one unconstrained key.
// The mesh shares that slider and adds a three-point weight limit.
foreach(bool bigEndian in new[]{false,true})
{
    const int start=704, block=256;
    byte[] bytes=new byte[start+160*block];
    void Word(int address,int value)
    {
        if(bigEndian)BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(address),value);
        else BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(address),value);
    }
    void Int(int slot,int index,int value)=>Word(start+slot*block+4*index,value);
    void Floats(int slot,params float[] values)
    {
        for(int i=0;i<values.Length;i++)Int(slot,i,BitConverter.SingleToInt32Bits(values[i]));
    }
    bytes[4]=5;bytes[5]=(byte)(bigEndian?1:0);
    for(int slot=0;slot<160;slot++)Word(64+4*slot,start+slot*block);
    foreach(var (i,n) in new[]{(1,1),(7,2),(9,2),(10,8),(14,2),(25,1),(26,2),(27,1),(28,1),(29,1),(30,1),(31,3)})Int(0,i,n);
    Int(114,0,1);Int(116,0,1);Int(118,0,2);Int(122,0,2);Int(122,1,2);
    Int(127,0,1);Int(129,0,1);Int(130,0,1);Int(124,1,1);Int(132,0,1);Int(134,0,3);
    Floats(135,-1,0,1);Floats(136,1,0.25f,0);Floats(77,0,0.75f);
    Int(60,1,2);Int(70,1,6);Floats(71,0,0,0.5f,-1,0,0,2,3);
    Floats(68,0,-0.25f);Floats(69,0,4);Floats(59,0,-0.5f);
    foreach(int slot in new[]{137,138,141,142})Int(slot,1,-1);
    // All three 5.0 object types share the constrained binding. Verify
    // each uses its own keyform table and retains its ordinary base form.
    Int(0,1,2);Int(17,1,1);Int(0,6,2);Int(0,8,2);Int(0,22,2);
    Int(0,19,1);Int(86,0,1);Int(87,0,0);
    foreach(var (countSlot,slot) in new[]{(32,143),(33,146),(34,149)})
    {
        Int(0,countSlot,1);Int(slot+1,0,1);Int(slot+2,0,1);
    }
    Floats(58,0,-6);Floats(100,0,-0.375f);
    Floats(61,0,-0.25f);Floats(62,0,-30);Floats(63,0,2);Floats(64,0,-3);Floats(65,0,0.25f);
    Int(139,1,-1);Int(140,1,-1);
    var runtime=new {binding_band_index=0,keyforms=new[]{new{draw_order=10},new{draw_order=20}},id="Badge"};
    var model=JsonSerializer.SerializeToElement(new {
        parameters=new[]{new{id="SleeveFold",@default=0.0},new{id="Turn",@default=0.8}},
        binding_bands=new[]{new{bindings=new[]{new{parameter_index=1,keys=new[]{-1,1}}}}},
        deformers=new object[]{runtime,new {runtime.binding_band_index,runtime.keyforms,parent_deformer_index=(int?)null}},
        art_meshes=new[]{runtime},parts=new[]{runtime},glues=new[]{new{runtime.binding_band_index,art_mesh_indices=new[]{2,3},intensities=new[]{0f,1f}}},canvas=new{pixels_per_unit=10}});
    XElement Field(string tag,string name,object value)=>new(tag,new XAttribute("xs.n",name),value);
    XElement Ref(string tag,string name,string reference)=>Field(tag,name,new XAttribute("xs.ref",reference));
    XElement Parameter(string id,string guid)=>new("CParameterSource",new XElement("CParameterId",new XAttribute("idstr",id)),
        Ref("CParameterGuid","guid",guid),Field("Type","paramType",new XAttribute("v","NORMAL")));
    XElement ObjectSource(bool mesh)
    {
        XElement Form(int i)=>new(mesh?"CArtMeshForm":"CWarpDeformerForm",Ref("CFormGuid","guid","#"+i),
            Ref("CoordType","coordType",mesh?"#5":"#6"),Field("float-array","positions",i==1?"0 1":"10 20"),
            Field("f","opacity",1),Field("i","drawOrder",i*10));
        return new XElement(mesh?"CArtMeshSource":"CWarpDeformerSource",new XAttribute("xs.id",mesh?"#7":"#8"),
            new XElement(mesh?"CDrawableId":"CDeformerId",new XAttribute("idstr",mesh?"Badge":"Cloth")),
            Field("carray_list","keyforms",new[]{Form(1),Form(2)}),
            Field("carray_list","_morphTargets",""),Field("carray_list","_constraints",""));
    }
    var warp=ObjectSource(false);var mesh=ObjectSource(true);
    var doc=new XDocument(new XElement("root",new XElement("shared",
        new XElement("CFormGuid",new XAttribute("xs.id","#1"),new XAttribute("note","Deformer0 keyform 0")),
        new XElement("CoordType",new XAttribute("xs.id","#5"),Field("s","coordName","Canvas")),
        new XElement("CoordType",new XAttribute("xs.id","#6"),Field("s","coordName","Local")),
        Parameter("SleeveFold","#3"),Parameter("Turn","#4"),warp,mesh)));
    XElement AdditionalSource(string tag,string idTag,string id,string sourceId,Func<int,XElement> makeForm)
    {
        return new XElement(tag,new XAttribute("xs.id",sourceId),
            new XElement(idTag,new XAttribute("idstr",id)),
            Field("carray_list","keyforms",new[]{makeForm(0),makeForm(1)}),
            Field("carray_list","_morphTargets",""),Field("carray_list","_constraints",""));
    }
    var shared=doc.Root!.Element("shared")!;
    shared.Add(new XElement("CFormGuid",new XAttribute("xs.id","#20"),new XAttribute("note","Deformer1 keyform 0")));
    var rotation=AdditionalSource("CRotationDeformerSource","CDeformerId","Arm","#21",i=>
        new XElement("CRotationDeformerForm",Ref("CFormGuid","guid",i==0?"#20":"#22"),Ref("CoordType","coordType","#5"),
            new XAttribute("angle",10*i),new XAttribute("scale",2*i),new XAttribute("originX",5*i),new XAttribute("originY",4*i),Field("f","opacity",1)));
    var part=AdditionalSource("CPartSource","CPartId","Badge","#23",i=>
        new XElement("CPartForm",Ref("CFormGuid","guid","#"+(30+i)),Field("i","drawOrder",10+i*10)));
    part.Add(Field("b","enableDrawOrderGroup",false));
    var glue=AdditionalSource("CGlueSource","CAffecterId","Glue0_2_3","#24",i=>
        new XElement("CGlueForm",Ref("CFormGuid","guid","#"+(32+i)),Field("f","intensity",i)));
    shared.Add(rotation,part,glue);
    new BlendShapes(bytes).Apply(doc,model);
    foreach(var obj in new[]{rotation,part,glue})
    {
        var target=obj.Descendants("KeyFormMorphTarget").Single();
        var forms=obj.Elements().Single(e=>(string?)e.Attribute("xs.n")=="keyforms").Elements().ToArray();
        var form=forms.Last();
        if(forms.Length!=3 || (string?)target.Element("CFormGuid")!.Attribute("xs.ref")!=(string?)form.Descendants("CFormGuid").Single().Attribute("xs.ref") ||
           obj.Descendants("MorphTargetBlendWeightConstraint").Count()!=3)
            throw new Exception($"Missing target references or weights on {obj.Name}.");
        if(obj==rotation && ((float)form.Attribute("angle")! != -20 || (float)form.Attribute("scale")! !=4.5f ||
            (float)form.Attribute("originX")! !=25 || (float)form.Attribute("originY")! != -26 || (float)form.Element("f")! !=0.75f))
            throw new Exception("Rotation key differs from fixture.");
        if(obj==part && ((int)form.Element("i")! !=14 || !(bool)part.Element("b")!))
            throw new Exception("Part draw order or group flag differs from fixture.");
        if(obj==glue && ((float)form.Element("f")! !=0.625f || (float)forms[1].Element("f")! !=1))
            throw new Exception("Glue intensity differs from fixture.");
    }
    foreach(var obj in new[]{warp,mesh})
    {
        var target=obj.Descendants("KeyFormMorphTarget").Single();
        var form=obj.Descendants().Single(e=>(string?)e.Attribute("xs.n")=="keyforms").Elements().Last();
        string Value(string field)=>form.Descendants().Single(e=>(string?)e.Attribute("xs.n")==field).Value;
        bool isMesh=obj==mesh;
        if(Value("positions")!=(isMesh?"30 50":"10.5 19") || Value("opacity")!=(isMesh?"0.75":"0.5") ||
           (isMesh && Value("drawOrder")!="24") || (float)target.Element("f")! !=0.75f ||
           target.Elements().First(e=>e.Name==obj.Name).Attribute("xs.ref")!.Value!=obj.Attribute("xs.id")!.Value)
            throw new Exception("Recovered target differs from fixture.");
        var points=obj.Descendants("MorphTargetBlendWeightConstraint").ToArray();
        var expected=isMesh?new[]{(-1f,1f),(0f,0.25f),(1f,0f)}:Array.Empty<(float,float)>();
        if(!points.Select(e=>((float)e.Elements("f").First(),(float)e.Elements("f").Last())).SequenceEqual(expected)
           ||points.Any(e=>!e.Elements("CParameterGuid").Select(p=>p.Attribute("xs.ref")!.Value).SequenceEqual(new[]{"#3","#4"})))
            throw new Exception("Recovered weight limit differs from fixture.");
    }
}
Console.WriteLine("Passed warp, ArtMesh, rotation, part and glue fixtures in both byte orders.");
