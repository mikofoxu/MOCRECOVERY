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

    internal List<string> Apply(XDocument doc,JsonElement model)
    {
        List<string> notes=[];
        if(bytes[4]<4)return notes;
        var parameters=model.GetProperty("parameters").EnumerateArray().ToArray();
        var sources=doc.Descendants("CParameterSource").Where(e=>e.HasElements).ToDictionary(e=>(string)e.Element("CParameterId")!.Attribute("idstr")!);
        var bindingOwners=new Dictionary<int,XElement>();
        int typed=0,warpTargets=0,meshTargets=0,constraintPoints=0;
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
        var warps=new Dictionary<int,(XElement Source,JsonElement Runtime)>();
        foreach(var source in doc.Descendants("CWarpDeformerSource").Where(x=>x.HasElements))
        {
            var first=Field(source,"keyforms").Elements().First();
            var guid=definitions[(string)Field(first,"guid").Attribute("xs.ref")!];
            string marker=((string?)guid.Attribute("note")??"").Split(' ')[0];
            if(!marker.StartsWith("Deformer")||!int.TryParse(marker[8..],out int general)||general<0||general>=At(0,1)||At(17,general)!=0)
                throw new InvalidDataException("Invalid warp index in recovered model.");
            warps.Add(At(18,general),(source,model.GetProperty("deformers")[general]));
        }
        var meshSources=doc.Descendants("CArtMeshSource").Where(e=>e.HasElements)
            .ToDictionary(e=>(string)e.Descendants("CDrawableId").Single().Attribute("idstr")!);
        foreach(bool mesh in new[]{false,true})
        for(int w=0;w<At(0,mesh?28:27);w++)
        {
            int objectSlot=mesh?128:125;
            int index=At(objectSlot,w);
            var runtime=mesh?model.GetProperty("art_meshes")[index]:warps[index].Runtime;
            var source=mesh?meshSources[runtime.GetProperty("id").GetString()!]:warps[index].Source;
            string name=(string)source.Descendants(mesh?"CDrawableId":"CDeformerId").Single().Attribute("idstr")!;
            var forms=Field(source,"keyforms");
            var ordinary=forms.Elements().Take(runtime.GetProperty("keyforms").GetArrayLength()).ToArray();
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
            var basePositions=Field(baseForm,"positions").Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(x=>float.Parse(x,CultureInfo.InvariantCulture)).ToArray();
            var morphs=Field(source,"_morphTargets");
            float scale=model.GetProperty("canvas").GetProperty("pixels_per_unit").GetSingle();
            var coordRef=(string)Field(baseForm,"coordType").Attribute("xs.ref")!;
            bool canvas=Field(shared.Elements("CoordType").Single(e=>(string?)e.Attribute("xs.id")==coordRef),"coordName").Value=="Canvas";
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
                var formIndices=Range(At(121,k),keyCount,At(0,mesh?9:7)).ToArray();
                for(int j=0;j<keyCount;j++)
                {
                    if(j==neutral)continue;
                    int f=formIndices[j],position=At(mesh?70:60,f);
                    var positions=Range(position,basePositions.Length,At(0,10)).Select((v,i)=>basePositions[i]+Float(71,v)*(canvas?scale:1)).ToArray();
                    var form=new XElement(baseForm);
                    Field(form,"positions").Value=string.Join(" ",positions.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
                    var opacity=Field(form,"opacity");
                    opacity.Value=(float.Parse(opacity.Value,CultureInfo.InvariantCulture)+Float(mesh?68:59,f)).ToString("R",CultureInfo.InvariantCulture);
                    if(bytes[4]>=5)
                    {
                        for(int channel=0;channel<2;channel++)
                        {
                            int colorIndex=At((mesh?141:137)+channel,f);
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
                    if(mesh)
                    {
                        double order=runtime.GetProperty("keyforms")[baseIndex].GetProperty("draw_order").GetDouble()+Float(69,f);
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
                    if(mesh)meshTargets++;else warpTargets++;
                }
            }
            forms.SetAttributeValue("count",forms.Elements().Count());morphs.SetAttributeValue("count",morphs.Elements().Count());
        }
        if(meshTargets+warpTargets>0)foreach(string type in new[]{"KeyFormMorphTarget","MorphTargetEditBaseParameterMap","MorphTargetBlendWeightConstraint"})
        {
            string path="com.live2d.cubism.doc.model.morphTarget."+type;
            if(!doc.Nodes().OfType<XProcessingInstruction>().Any(x=>x.Data==path))doc.Root.AddBeforeSelf(new XProcessingInstruction("import",path));
        }
        int other=(bytes[4]>=5?At(0,32)+At(0,33)+At(0,34):0);
        if(other>0)notes.Add($"Other blend-shape target objects present but unsupported: {other}.");
        notes.Add($"MOC3 blend-shape parameters: {typed}; editable warp targets restored: {warpTargets}; editable ArtMesh targets restored: {meshTargets}; weight constraint points restored: {constraintPoints}.");
        return notes;
    }
}
