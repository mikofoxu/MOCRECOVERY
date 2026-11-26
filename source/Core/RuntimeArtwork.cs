using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace MocRecovery;

internal static class RuntimeArtwork
{
    static XElement F(XElement x,string name)=>x.Descendants().First(e=>(string?)e.Attribute("xs.n")==name);
    static string Ref(XElement e)=>(string)e.Attribute("xs.ref")!;
    static void Append(XElement list,XElement child){list.Add(child);list.SetAttributeValue("count",list.Elements().Count());}

    // Rasterize UV triangles at atlas pixel centers. Scanlines avoid a second full-size atlas buffer.
    internal static (byte[] Data,int X,int Y,int Width,int Height)? Crop(JsonElement mesh,FileStream decoded,int width,int height,CancellationToken token=default)
    {
        var uv=mesh.GetProperty("uvs").EnumerateArray().Select(p=>(X:p[0].GetDouble()*width,Y:p[1].GetDouble()*height)).ToArray();
        int[] indices=mesh.GetProperty("triangle_indices").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
        if(indices.Length==0)return null;
        var vertices=indices.Select(i=>uv[i]).ToArray();
        int left=Math.Clamp((int)Math.Floor(vertices.Min(v=>v.X)),0,width),right=Math.Clamp((int)Math.Ceiling(vertices.Max(v=>v.X)),0,width);
        int top=Math.Clamp((int)Math.Floor(vertices.Min(v=>v.Y)),0,height),bottom=Math.Clamp((int)Math.Ceiling(vertices.Max(v=>v.Y)),0,height);
        if(left>=right||top>=bottom)return null;
        var spans=new List<(int Start,int End)>?[bottom-top];
        for(int i=0;i<vertices.Length;i+=3)
        {
            token.ThrowIfCancellationRequested();
            var a=vertices[i];var b=vertices[i+1];var c=vertices[i+2];
            int first=Math.Max(top,(int)Math.Ceiling(Math.Min(a.Y,Math.Min(b.Y,c.Y))-.5));
            int end=Math.Min(bottom,(int)Math.Ceiling(Math.Max(a.Y,Math.Max(b.Y,c.Y))-.5));
            for(int y=first;y<end;y++)
            {
                double scan=y+.5,lo=double.PositiveInfinity,hi=double.NegativeInfinity;
                void Edge((double X,double Y) u,(double X,double Y) v)
                {
                    if((u.Y>scan)==(v.Y>scan))return;
                    double x=u.X+(scan-u.Y)*(v.X-u.X)/(v.Y-u.Y);
                    lo=Math.Min(lo,x);hi=Math.Max(hi,x);
                }
                Edge(a,b);Edge(b,c);Edge(c,a);
                if(hi<=lo)continue;
                int x0=Math.Max(left,(int)Math.Ceiling(lo-.5)),x1=Math.Min(right,(int)Math.Floor(hi-.5)+1);
                if(x1>x0)(spans[y-top]??=[]).Add((x0-left,x1-left));
            }
        }
        if(spans.All(row=>row==null))return null;
        int w=right-left;byte[] raw=new byte[4*w];
        byte[] png=PsdReader.EncodeRows(w,bottom-top,(y,row)=>{
            token.ThrowIfCancellationRequested();
            Array.Clear(row);
            if(spans[y]==null)return;
            decoded.Position=4L*((long)(y+top)*width+left);
            decoded.ReadExactly(raw);
            foreach(var (start,end) in spans[y]!)Buffer.BlockCopy(raw,4*start,row,4*start,4*(end-start));
        },null);
        return(png,left,top,w,bottom-top);
    }

    internal static List<string> Apply(XDocument doc,Dictionary<string,byte[]> entries,JsonElement model,IReadOnlyList<string> pages,string folder,Action<string> stage,CancellationToken token=default)
    {
        Directory.CreateDirectory(folder);
        var shared=doc.Root!.Element("shared")!;
        var objects=doc.Descendants().Where(e=>e.Attribute("xs.id")!=null).ToDictionary(e=>(string)e.Attribute("xs.id")!);
        int next=objects.Keys.Select(k=>int.Parse(k[1..])).Max()+1;
        string Install(XElement e){string id="#"+next++;e.SetAttributeValue("xs.id",id);e.Attribute("xs.idx")?.Remove();shared.Add(e);return id;}
        var images=doc.Descendants("CModelImage").Where(e=>e.HasElements).ToDictionary(e=>Ref(F(e,"guid")));
        var atlasImages=doc.Descendants("ModelImageEntry").Where(e=>e.HasElements).Select(e=>Ref(F(e,"modelImageGuid"))).ToHashSet();
        var sources=doc.Descendants("CArtMeshSource").Where(e=>e.HasElements).ToDictionary(e=>(string)e.Descendants("CDrawableId").Single().Attribute("idstr")!);
        var inventory=new List<object>();var notes=new List<string>();int created=0,psd=0;
        foreach(var group in model.GetProperty("art_meshes").EnumerateArray().GroupBy(m=>m.GetProperty("texture_index").GetInt32()))
        {
            int page=group.Key;
            if(page>=pages.Count||!File.Exists(pages[page])){notes.Add($"Atlas page {page} missing; skipped {group.Count()} mesh crops.");continue;}
            using var raw=new FileStream(Path.Combine(folder,Guid.NewGuid()+".tmp"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,65536,FileOptions.DeleteOnClose);
            byte[] png=File.ReadAllBytes(pages[page]);int width,height;
            try{(width,height)=PngRows.Size(png);PngRows.Read(png,(_,row)=>{token.ThrowIfCancellationRequested();raw.Write(row); });}
            catch(InvalidDataException e){notes.Add($"Atlas page {page} skipped: {e.Message}");continue;}
            foreach(var mesh in group)
            {
                string id=mesh.GetProperty("id").GetString()!;
                var source=sources[id];var ext=objects[Ref(source.Descendants("CTextureInputExtension").Single(e=>e.Attribute("xs.ref")!=null))];
                var inputs=F(ext,"_textureInputs");var input=objects[Ref(inputs.Elements("CTextureInput_ModelImage").Single())];
                var sourceGuid=F(input,"_modelImageGuid");string previous=Ref(sourceGuid);
                if(!atlasImages.Contains(previous)){psd++;inventory.Add(new{id,page,source="existing source image"});continue;}
                stage("Extracting atlas artwork: "+id);
                var crop=Crop(mesh,raw,width,height,token);
                if(crop==null){notes.Add($"Atlas crop skipped for {id}: no covered texels.");continue;}
                var (data,x,y,w,h)=crop.Value;string file=$"atlas-mesh-{created:D4}.png";
                var template=images[previous];var layerTemplate=objects[Ref(template.Descendants("CLayer").Single())];
                var layer=new XElement(layerTemplate);var resource=new XElement(objects[Ref(F(layer,"imageResource"))]);
                resource.SetAttributeValue("width",w);resource.SetAttributeValue("height",h);resource.SetAttributeValue("imageFileBuf_size",data.Length);
                F(resource,"imageFileBuf").SetAttributeValue("path",file);string rid=Install(resource);
                string layerId=Install(layer);
                F(layer,"imageResource").SetAttributeValue("xs.ref",rid);F(layer,"guid").SetAttributeValue("uuid",Guid.NewGuid());
                F(layer,"name").Value=id;F(layer,"layerName").Value=id;F(layer,"layerId").Value="atlas-mesh-"+created;
                F(layer,"layerIdValue_testImpl").Value=(1000000+created).ToString();
                var bounds=F(layer,"boundsOnImageDoc");
                foreach(var (key,value) in new[]{("x",x),("y",y),("width",w),("height",h)})F(bounds,key).Value=value.ToString();
                foreach(var list in new[]{F(objects[Ref(F(layer,"group"))],"_children"),F(objects[Ref(F(layer,"_layeredImage"))],"_layerEntryList")})
                    Append(list,new XElement("CLayer",new XAttribute("xs.ref",layerId)));
                var image=new XElement(template);string guid=Install(new XElement("CModelImageGuid",new XAttribute("uuid",Guid.NewGuid()),new XAttribute("note",id)));
                F(image,"guid").SetAttributeValue("xs.ref",guid);F(image,"name").Value=id;F(image,"memo").Value="ArtMesh pixels extracted from runtime atlas page "+page;
                image.Descendants("CLayer").Single().SetAttributeValue("xs.ref",layerId);
                foreach(var res in image.Descendants("CImageResource"))res.SetAttributeValue("xs.ref",rid);
                foreach(var size in image.Descendants("CSize")){size.SetAttributeValue("width",w);size.SetAttributeValue("height",h);}
                var material=F(image,"_materialLocalToCanvasTransform");material.SetAttributeValue("m02",x);material.SetAttributeValue("m12",y);
                Append(template.Parent!,image);
                var atlas=objects[Ref(inputs.Elements("CTextureInput_TextureAtlasRegion").Single())];
                string[] keys=["m00","m01","m02","m10","m11","m12"];
                double[] Matrix(XElement m)=>keys.Select(k=>double.Parse((string)m.Attribute(k)!,CultureInfo.InvariantCulture)).ToArray();
                var placement=PsdTextureRelinker.Multiply(Matrix(F(atlas,"optionalTransformOnCanvas")),Matrix(F(atlas,"inputImageLocalToCanvasTransform")));
                var transform=F(input,"optionalTransformOnCanvas");for(int k=0;k<6;k++)transform.SetAttributeValue(keys[k],placement[k]);
                sourceGuid.SetAttributeValue("xs.ref",guid);
                entries.Add(file,data);File.WriteAllBytes(Path.Combine(folder,file),data);
                inventory.Add(new{id,page,file,x,y,width=w,height=h});created++;
            }
        }
        File.WriteAllText(Path.Combine(folder,"artwork.json"),JsonSerializer.Serialize(inventory,new JsonSerializerOptions{WriteIndented=true}));
        notes.Add($"Individual ArtMesh atlas source images embedded and exported: {created}; existing PSD source links retained: {psd}.");
        return notes;
    }
}
