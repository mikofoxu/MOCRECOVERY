using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace MocRecovery;

// MOC3 4.2+ tables. A blend key stores offsets relative to the editing base.
internal sealed class BlendShapes(byte[] bytes)
{
    int Word(int offset)
    {
        if (offset < 0 || offset > bytes.Length - 4) throw new InvalidDataException("Truncated blend shape table.");
        var span = bytes.AsSpan(offset, 4);
        return bytes[5] == 0 ? BinaryPrimitives.ReadInt32LittleEndian(span) : BinaryPrimitives.ReadInt32BigEndian(span);
    }
    int At(int slot,int index)
    {
        int start=Word(64+4*slot);
        if(start<704 || index<0) throw new InvalidDataException("Invalid blend shape offset.");
        int address=checked(start+4*index);
        // Sections may be empty and share offsets. Limit access to the current table.
        int next=Word(64+4*(slot+1));
        if(next>=start && address>next-4)
            throw new InvalidDataException("Blend shape reference exceeds its section.");
        return Word(address);
    }
    float Float(int slot,int index)
    {
        float value=BitConverter.Int32BitsToSingle(At(slot,index));
        return float.IsFinite(value)?value:throw new InvalidDataException("Non-finite blend shape value.");
    }
    static IEnumerable<int> Range(int first,int count,int limit)
    {
        if(count<0 || (count>0 && (first<0 || first>(long)limit-count))) throw new InvalidDataException("Blend shape range exceeds its table.");
        return Enumerable.Range(count==0?0:first,count);
    }
    static XElement Field(XElement e,string n)=>e.Descendants().First(x=>(string?)x.Attribute("xs.n")==n);
    static XElement Ref(string tag,string name,string id)=>new(tag,new XAttribute("xs.n",name),new XAttribute("xs.ref",id));

    enum Kind { Warp, ArtMesh, Part, Rotation, Glue }

    static float RotationScale(JsonElement rotation,JsonElement model,float pixelsPerUnit)
    {
        var deformers=model.GetProperty("deformers");
        var parent=rotation.GetProperty("parent_deformer_index");
        for(int depth=0;parent.ValueKind!=JsonValueKind.Null;depth++)
        {
            if(depth>=deformers.GetArrayLength())throw new InvalidDataException("Cyclic deformer hierarchy.");
            var ancestor=deformers[parent.GetInt32()];
            if(ancestor.GetProperty("kind").GetString()=="rotation")return 1;
            parent=ancestor.GetProperty("parent_deformer_index");
        }
        return pixelsPerUnit==0?1:pixelsPerUnit;
    }

    void AddDelta(XElement form,string name,int slot,int key)
    {
        var value=Field(form,name);
        value.SetValue((float)value+Float(slot,key));
    }

    internal List<string> Apply(XDocument doc,JsonElement model)
    {
        List<string> notes=[];
        if(bytes[4]<4)return notes;
        var parameters=model.GetProperty("parameters").EnumerateArray().ToArray();
        var sources=doc.Descendants("CParameterSource").Where(e=>e.HasElements).ToDictionary(e=>(string)e.Element("CParameterId")!.Attribute("idstr")!);
        var bindingOwners=new Dictionary<int,XElement>();
        int typed=0,constraintPoints=0;
        var totals=new int[5];
        for(int p=0;p<parameters.Length;p++)
        {
            if(At(114,p)!=1)continue;
            var source=sources[parameters[p].GetProperty("id").GetString()!];
            Field(source,"paramType").SetAttributeValue("v","MORPH_TARGET");
            typed++;
            foreach(int b in Range(At(115,p),At(116,p),At(0,25)))bindingOwners.Add(b,source);
        }
        var shared=doc.Root!.Element("shared")!;
        int nextId=doc.Descendants().Attributes("xs.id").Select(a=>int.Parse(a.Value[1..])).Max()+1;
        // The engine records the MOC's general deformer index on its ordinary forms.
        // Use that stable index even when editor labels have changed.
        var definitions=shared.Elements("CFormGuid").Where(x=>x.Attribute("xs.id")!=null).ToDictionary(x=>(string)x.Attribute("xs.id")!);
        var deformers=new Dictionary<(Kind,int),(XElement Source,JsonElement Runtime)>();
        foreach(var source in shared.Elements().Where(e=>e.Name=="CWarpDeformerSource"||e.Name=="CRotationDeformerSource"))
        {
            var first=Field(source,"keyforms").Elements().First();
            var guid=definitions[(string)Field(first,"guid").Attribute("xs.ref")!];
            string marker=((string?)guid.Attribute("note")??"").Split(' ')[0];
            bool rotation=source.Name=="CRotationDeformerSource";
            if(!marker.StartsWith("Deformer")||!int.TryParse(marker[8..],out int general)||general<0||general>=At(0,1)||At(17,general)!=(rotation?1:0))
                throw new InvalidDataException("Invalid deformer index in recovered model.");
            deformers.Add((rotation?Kind.Rotation:Kind.Warp,At(18,general)),(source,model.GetProperty("deformers")[general]));
        }
        Dictionary<string,XElement> Sources(string tag,string idTag)=>shared.Elements(tag)
            .ToDictionary(e=>(string)e.Descendants(idTag).Single().Attribute("idstr")!);
        var meshes=Sources("CArtMeshSource","CDrawableId");
        var parts=Sources("CPartSource","CPartId");
        var glues=Sources("CGlueSource","CAffecterId");
        var drawOrderGroups=new HashSet<int>();
        if(bytes[4]>=5 && At(0,32)>0)
            foreach(int i in Range(0,At(0,19),At(0,19)))
                if(At(86,i)==1)drawOrderGroups.Add(At(87,i));

        foreach(var (kind,countSlot,objectSlot,formCountSlot) in new[]{
            (Kind.Warp,27,125,7),(Kind.ArtMesh,28,128,9),(Kind.Part,32,143,6),
            (Kind.Rotation,33,146,8),(Kind.Glue,34,149,22)})
        {
            if(bytes[4]<5 && countSlot>=32)continue;
            for(int w=0;w<At(0,countSlot);w++)
            {
                int index=At(objectSlot,w);
                XElement source;
                JsonElement runtime;
                if(kind is Kind.Warp or Kind.Rotation)
                    (source,runtime)=deformers[(kind,index)];
                else if(kind==Kind.Glue)
                {
                    runtime=model.GetProperty("glues")[index];
                    var pair=runtime.GetProperty("art_mesh_indices");
                    source=glues[$"Glue{index}_{pair[0].GetInt32()}_{pair[1].GetInt32()}"];
                }
                else
                {
                    bool part=kind==Kind.Part;
                    runtime=model.GetProperty(part?"parts":"art_meshes")[index];
                    source=(part?parts:meshes)[runtime.GetProperty("id").GetString()!];
                    if(part && drawOrderGroups.Contains(index))Field(source,"enableDrawOrderGroup").SetValue(true);
                }
                string name=(string)source.Descendants().Single(e=>e.Attribute("idstr")!=null).Attribute("idstr")!;
                var forms=Field(source,"keyforms");
                var ordinary=forms.Elements().Take(runtime.GetProperty(kind==Kind.Glue?"intensities":"keyforms").GetArrayLength()).ToArray();
                int baseIndex=0,stride=1;
                if(runtime.GetProperty("binding_band_index") is var band && band.ValueKind!=JsonValueKind.Null)
                {
                    foreach(var axis in model.GetProperty("binding_bands")[band.GetInt32()].GetProperty("bindings").EnumerateArray())
                    {
                        var keys=axis.GetProperty("keys").EnumerateArray().Select(e=>e.GetDouble()).ToArray();
                        var center=parameters[axis.GetProperty("parameter_index").GetInt32()].GetProperty("default").GetDouble();
                        int nearest=Enumerable.Range(0,keys.Length).MinBy(i=>Math.Abs(keys[i]-center));
                        baseIndex=checked(baseIndex+nearest*stride);
                        stride=checked(stride*keys.Length);
                    }
                }
                if(stride!=ordinary.Length)throw new InvalidDataException($"Incomplete ordinary keyform grid on {name}.");
                var baseForm=ordinary[baseIndex];
                bool geometry=kind is Kind.ArtMesh or Kind.Warp;
                bool deforms=geometry || kind==Kind.Rotation;
                var basePositions=geometry?Field(baseForm,"positions").Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)
                    .Select(x=>float.Parse(x,CultureInfo.InvariantCulture)).ToArray():Array.Empty<float>();
                var morphs=Field(source,"_morphTargets");
                float scale=model.GetProperty("canvas").GetProperty("pixels_per_unit").GetSingle();
                bool canvas=false;
                if(deforms)
                {
                    var coordRef=(string)Field(baseForm,"coordType").Attribute("xs.ref")!;
                    canvas=Field(shared.Elements("CoordType").Single(e=>(string?)e.Attribute("xs.id")==coordRef),"coordName").Value=="Canvas";
                }
                float rotationScale=kind==Kind.Rotation?RotationScale(runtime,model,scale):1;
                foreach(int k in Range(At(objectSlot+1,w),At(objectSlot+2,w),At(0,26)))
                {
                    int binding=At(120,k);
                    var parameter=bindingOwners[binding];
                    var constraints=Field(source,"_constraints");
                    foreach(int entry in Range(At(123,k),At(124,k),At(0,29)))
                    {
                        int curve=Range(At(131,entry),1,At(0,30)).Single();
                        int controller=Range(At(132,curve),1,parameters.Length).Single();
                        var driver=sources[parameters[controller].GetProperty("id").GetString()!];
                        foreach(int point in Range(At(133,curve),At(134,curve),At(0,31)))
                        {
                            constraints.Add(new XElement("MorphTargetBlendWeightConstraint",
                                Ref("CParameterGuid","morphTargetParameterGuid",(string)Field(parameter,"guid").Attribute("xs.ref")!),
                                Ref("CParameterGuid","constraintParameterGuid",(string)Field(driver,"guid").Attribute("xs.ref")!),
                                new XElement("f",new XAttribute("xs.n","constraintParameterValue"),Float(135,point)),
                                new XElement("f",new XAttribute("xs.n","blendWeight"),Float(136,point))));
                            constraintPoints++;
                        }
                    }
                    constraints.SetAttributeValue("count",constraints.Elements().Count());
                    int keyCount=At(118,binding),neutral=At(119,binding);
                    if(keyCount!=At(122,k)||neutral<0||neutral>=keyCount)throw new InvalidDataException("Inconsistent blend shape keys.");
                    var keyIndices=Range(At(117,binding),keyCount,At(0,14)).ToArray();
                    var formIndices=Range(At(121,k),keyCount,At(0,formCountSlot)).ToArray();
                    for(int j=0;j<keyCount;j++)
                    {
                        if(j==neutral)continue;
                        int f=formIndices[j];
                        var form=new XElement(baseForm);
                        if(geometry)
                        {
                            int position=At(kind==Kind.ArtMesh?70:60,f);
                            var positions=Range(position,basePositions.Length,At(0,10))
                                .Select((v,i)=>basePositions[i]+Float(71,v)*(canvas?scale:1));
                            Field(form,"positions").Value=string.Join(" ",positions.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
                        }
                        if(kind==Kind.Rotation)
                        {
                            foreach(var (attribute,slot,factor) in new[]{("angle",62,1f),("originX",63,canvas?scale:1),
                                ("originY",64,canvas?scale:1),("scale",65,rotationScale)})
                                form.SetAttributeValue(attribute,(float)form.Attribute(attribute)!+Float(slot,f)*factor);
                        }
                        if(kind==Kind.Glue)AddDelta(form,"intensity",100,f);
                        if(deforms)AddDelta(form,"opacity",kind switch {Kind.ArtMesh=>68,Kind.Rotation=>61,_=>59},f);
                        if(deforms && bytes[4]>=5)
                        {
                            int colorSlot=kind switch {Kind.ArtMesh=>141,Kind.Rotation=>139,_=>137};
                            for(int channel=0;channel<2;channel++)
                            {
                                int colorIndex=At(colorSlot+channel,f);
                                if(colorIndex<0)continue;
                                var color=Field(form,channel==0?"multiplyColor":"screenColor");
                                int component=0;
                                foreach(string rgb in new[]{"red","green","blue"})
                                {
                                    var delta=Float(108+3*channel+component++,colorIndex);
                                    color.SetAttributeValue(rgb,(float)color.Attribute(rgb)!+delta);
                                }
                            }
                        }
                        if(kind is Kind.ArtMesh or Kind.Part)
                        {
                            double order=runtime.GetProperty("keyforms")[baseIndex].GetProperty("draw_order").GetDouble()+Float(kind==Kind.Part?58:69,f);
                            Field(form,"drawOrder").Value=Math.Round(order,MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
                        }
                        string guid="#"+nextId++;
                        shared.Add(new XElement("CFormGuid",new XAttribute("xs.id",guid),new XAttribute("uuid",Guid.NewGuid()),new XAttribute("note",name+" blend key")));
                        Field(form,"guid").SetAttributeValue("xs.ref",guid);
                        forms.Add(form);
                        morphs.Add(new XElement("KeyFormMorphTarget",Ref(source.Name.LocalName,"owner",(string)source.Attribute("xs.id")!),
                            Ref("CFormGuid","keyformGuid",guid),Ref("CParameterGuid","parameterGuid",(string)Field(parameter,"guid").Attribute("xs.ref")!),
                            new XElement("f",new XAttribute("xs.n","keyValue"),Float(77,keyIndices[j])),
                            new XElement("MorphTargetEditBaseParameterMap",new XAttribute("xs.n","editBaseParameterMap"),new XElement("null",new XAttribute("xs.n","baseParameterMap")))));
                        totals[(int)kind]++;
                    }
                }
                forms.SetAttributeValue("count",forms.Elements().Count());morphs.SetAttributeValue("count",morphs.Elements().Count());
            }
        }
        if(totals.Sum()>0)foreach(string type in new[]{"KeyFormMorphTarget","MorphTargetEditBaseParameterMap","MorphTargetBlendWeightConstraint"})
        {
            string path="com.live2d.cubism.doc.model.morphTarget."+type;
            if(!doc.Nodes().OfType<XProcessingInstruction>().Any(x=>x.Data==path))doc.Root.AddBeforeSelf(new XProcessingInstruction("import",path));
        }
        notes.Add($"MOC3 blend-shape parameters: {typed}; editable warp targets restored: {totals[0]}; editable ArtMesh targets restored: {totals[1]}; editable part targets restored: {totals[2]}; editable rotation targets restored: {totals[3]}; editable glue targets restored: {totals[4]}; weight constraint points restored: {constraintPoints}.");
        return notes;
    }
}
