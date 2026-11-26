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
        int typed=0,targets=0;
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
        var warps=new Dictionary<int,XElement>();
        foreach(var source in doc.Descendants("CWarpDeformerSource").Where(x=>x.HasElements))
        {
            var first=Field(source,"keyforms").Elements().First();
            var guid=definitions[(string)Field(first,"guid").Attribute("xs.ref")!];
            string marker=((string?)guid.Attribute("note")??"").Split(' ')[0];
            if(!marker.StartsWith("Deformer")||!int.TryParse(marker[8..],out int general)||general<0||general>=At(0,1)||At(17,general)!=0)
                throw new InvalidDataException("Invalid warp index in recovered model.");
            warps.Add(At(18,general),source);
        }
        for(int w=0;w<At(0,27);w++)
        {
            var warp=warps[At(125,w)];
            string name=(string)Field(warp,"id").Attribute("idstr")!;
            var forms=Field(warp,"keyforms");
            var baseForm=forms.Elements().First();
            if(forms.Elements().Count()!=1){notes.Add($"Blend shapes on {name} skipped: multiple ordinary forms require an editing-base selection.");continue;}
            var basePositions=Field(baseForm,"positions").Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(x=>float.Parse(x,CultureInfo.InvariantCulture)).ToArray();
            var morphs=Field(warp,"_morphTargets");
            float scale=model.GetProperty("canvas").GetProperty("pixels_per_unit").GetSingle();
            var coordRef=(string)Field(baseForm,"coordType").Attribute("xs.ref")!;
            bool canvas=Field(shared.Elements("CoordType").Single(e=>(string?)e.Attribute("xs.id")==coordRef),"coordName").Value=="Canvas";
            foreach(int k in Range(At(126,w),At(127,w),At(0,26)))
            {
                if(At(124,k)!=0){notes.Add($"Blend binding {k} on {name} skipped: constrained weights.");continue;}
                int binding=At(120,k);
                var parameter=bindingOwners[binding];
                int keyCount=At(118,binding),neutral=At(119,binding);
                if(keyCount!=At(122,k)||neutral<0||neutral>=keyCount)throw new InvalidDataException("Inconsistent blend shape keys.");
                var keyIndices=Range(At(117,binding),keyCount,At(0,14)).ToArray();
                var formIndices=Range(At(121,k),keyCount,At(0,7)).ToArray();
                for(int j=0;j<keyCount;j++)
                {
                    if(j==neutral)continue;
                    int f=formIndices[j],position=At(60,f);
                    var positions=Range(position,basePositions.Length,At(0,10)).Select((v,i)=>basePositions[i]+Float(71,v)*(canvas?scale:1)).ToArray();
                    var form=new XElement(baseForm);
                    Field(form,"positions").Value=string.Join(" ",positions.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));
                    var opacity=Field(form,"opacity");
                    opacity.Value=(float.Parse(opacity.Value,CultureInfo.InvariantCulture)+Float(59,f)).ToString("R",CultureInfo.InvariantCulture);
                    string guid="#"+nextId++;
                    shared.Add(new XElement("CFormGuid",new XAttribute("xs.id",guid),new XAttribute("uuid",Guid.NewGuid()),new XAttribute("note",name+" blend key")));
                    Field(form,"guid").SetAttributeValue("xs.ref",guid);
                    forms.Add(form);
                    morphs.Add(new XElement("KeyFormMorphTarget",Ref("CWarpDeformerSource","owner",(string)warp.Attribute("xs.id")!),
                        Ref("CFormGuid","keyformGuid",guid),Ref("CParameterGuid","parameterGuid",(string)Field(parameter,"guid").Attribute("xs.ref")!),
                        new XElement("f",new XAttribute("xs.n","keyValue"),Float(77,keyIndices[j])),
                        new XElement("MorphTargetEditBaseParameterMap",new XAttribute("xs.n","editBaseParameterMap"),new XElement("null",new XAttribute("xs.n","baseParameterMap")))));
                    targets++;
                }
            }
            forms.SetAttributeValue("count",forms.Elements().Count());morphs.SetAttributeValue("count",morphs.Elements().Count());
        }
        if(targets>0)foreach(string type in new[]{"KeyFormMorphTarget","MorphTargetEditBaseParameterMap"})
        {
            string path="com.live2d.cubism.doc.model.morphTarget."+type;
            if(!doc.Nodes().OfType<XProcessingInstruction>().Any(x=>x.Data==path))doc.Root.AddBeforeSelf(new XProcessingInstruction("import",path));
        }
        int other=At(0,28)+(bytes[4]>=5?At(0,32)+At(0,33)+At(0,34):0);
        if(other>0)notes.Add($"Other blend-shape target objects present but unsupported: {other}.");
        notes.Add($"MOC3 blend-shape parameters: {typed}; editable warp targets restored: {targets}.");
        return notes;
    }
}
