using System.Text;
using System.Text.Json;
using System.Xml.Linq;
namespace MocRecovery;
public sealed record PsdImportResult(int Layers,int Imported,int NameMatches,List<string> Notes)
{public int ArtworkMatches {get;init;} public int RelinkedTextures {get;init;}}
public static class PsdImporter
{
    static string? N(XElement e)=>(string?)e.Attribute("xs.n");
    static XElement Field(XElement e,string n)=>e.DescendantsAndSelf().First(x=>N(x)==n);
    static XElement Ref(string tag,string id)=>new(tag,new XAttribute("xs.ref",id));
    public static PsdImportResult Apply(string archivePath,string psdPath,string exportDirectory,bool matchArtwork=false,IReadOnlyList<string>? textures=null,Action<string>? update=null,bool relinkTextures=false,Action<int>? progress=null)
    {
        matchArtwork|=relinkTextures;
        using var psd=new PsdReader(psdPath);var entries=CaffArchive.Read(archivePath);var doc=XDocument.Load(new MemoryStream(entries["main.xml"]));var shared=doc.Root!.Element("shared")!;
        Directory.CreateDirectory(exportDirectory);
        int next=doc.Descendants().Attributes("xs.id").Select(a=>int.Parse(a.Value.TrimStart('#'))).Max()+1;
        string Id()=>"#"+next++;
        XElement Definition(string tag)=>shared.Elements(tag).First(e=>e.HasElements);
        string Install(XElement e){string id=Id();e.SetAttributeValue("xs.id",id);e.Attribute("xs.idx")?.Remove();shared.Add(e);return id;}
        string GuidDef(string tag,string name){var e=new XElement(tag,new XAttribute("uuid",Guid.NewGuid()),new XAttribute("note",name));return Install(e);}
        void Refs(XElement e,string tag,string id){foreach(var x in e.DescendantsAndSelf(tag).Where(x=>x.Attribute("xs.ref")!=null))x.SetAttributeValue("xs.ref",id);}
        void Count(XElement e)=>e.SetAttributeValue("count",e.Elements().Count());
        var layerTemplate=new XElement(Definition("CLayer"));var groupTemplate=new XElement(Definition("CLayerGroup"));var imageTemplate=new XElement(Definition("CLayeredImage"));
        var modelGroupTemplate=new XElement(Definition("CModelImageGroup"));var modelTemplate=new XElement(modelGroupTemplate.Descendants("CModelImage").First());
        string imageGuid=GuidDef("CLayeredImageGuid",Path.GetFileName(psdPath));var image=new XElement(imageTemplate);string imageId=Install(image);
        var root=new XElement(groupTemplate);string rootId=Install(root);Field(root,"name").Value="PSD source artwork";Field(root,"_children").RemoveNodes();Count(Field(root,"_children"));Refs(root,"CLayeredImage",imageId);Field(root,"guid").SetAttributeValue("uuid",Guid.NewGuid());
        var modelGroup=new XElement(modelGroupTemplate);string modelGroupId=Install(modelGroup);Field(modelGroup,"groupName").Value="PSD source artwork";Refs(modelGroup,"CLayeredImageGuid",imageGuid);var modelList=Field(modelGroup,"_modelImages");modelList.RemoveNodes();Count(modelList);
        Field(image,"name").Value=Path.GetFileName(psdPath);Field(image,"width").Value=psd.Width.ToString();Field(image,"height").Value=psd.Height.ToString();Field(image,"psdFile").Value=Path.GetFullPath(psdPath);Refs(image,"CLayeredImageGuid",imageGuid);Refs(image,"CLayeredImage",imageId);Refs(image,"CLayerGroup",rootId);var layerList=Field(image,"_layerEntryList");layerList.RemoveNodes();layerList.Add(Ref("CLayerGroup",rootId));
        var groups=new Dictionary<int,(string Id,XElement Element)>();var notes=new List<string>();var inventory=new List<object>();int imported=0,matches=0;var artworkCandidates=new List<ArtworkCandidate>();var modelImages=new Dictionary<int,XElement>();
        foreach(var l in psd.Layers.Where(l=>l.Section is 1 or 2))
        {
            var g=new XElement(groupTemplate);string id=Install(g);Field(g,"name").Value=l.Name;Field(g,"guid").SetAttributeValue("uuid",Guid.NewGuid());Field(g,"isVisible").Value=l.Visible?"true":"false";Field(g,"opacity255").Value=l.Opacity.ToString();Field(g,"_children").RemoveNodes();Count(Field(g,"_children"));Refs(g,"CLayeredImage",imageId);groups[l.Index]=(id,g);layerList.Add(Ref("CLayerGroup",id));
        }
        void Attach(XElement e,string id,string tag,int? parent)
        {
            var target=parent.HasValue&&groups.TryGetValue(parent.Value,out var g)?g:(rootId,root);
            var groupField=Field(e,"group");groupField.ReplaceWith(new XElement("CLayerGroup",new XAttribute("xs.n","group"),new XAttribute("xs.ref",target.Item1)));
            Field(target.Item2,"_children").Add(Ref(tag,id));Count(Field(target.Item2,"_children"));
        }
        foreach(var l in psd.Layers.Where(l=>l.Section is 1 or 2))Attach(groups[l.Index].Element,groups[l.Index].Id,"CLayerGroup",l.Parent);
        var meshSources=doc.Descendants("CArtMeshSource").Where(e=>e.HasElements).ToArray();
        string Normalize(string s)=>s.EndsWith(".png",StringComparison.OrdinalIgnoreCase)?s[..^4].Trim().ToLowerInvariant():s.Trim().ToLowerInvariant();
        var unique=psd.Layers.Where(l=>l.Section==0).GroupBy(l=>Normalize(l.Name)).ToDictionary(g=>g.Key,g=>g.ToArray());
        int completedLayers=0,totalLayers=psd.Layers.Count(l=>l.Section==0);
        foreach(var l in psd.Layers.Where(l=>l.Section==0))
        {
            progress?.Invoke(completedLayers*65/Math.Max(1,totalLayers));
            update?.Invoke($"Importing PSD layer {l.Index+1}/{psd.Layers.Count}: {l.Name}…");
            notes.AddRange(l.Warnings.Select(w=>$"PSD {l.Name}: {w}"));
            try
            {
                byte[] png=psd.Png(l);string file=$"psd-layer-{l.Index:D4}.png";entries[file]=png;File.WriteAllBytes(Path.Combine(exportDirectory,file),png);
                var resource=new XElement("CImageResource",new XAttribute("width",l.Width),new XAttribute("height",l.Height),new XAttribute("type","INT_ARGB"),new XAttribute("imageFileBuf_size",png.Length),new XAttribute("previewFileBuf_size",0),new XElement("file",new XAttribute("xs.n","imageFileBuf"),new XAttribute("path",file)));string resourceId=Install(resource);
                var layer=new XElement(layerTemplate);string layerId=Install(layer);Field(layer,"name").Value=l.Name;Field(layer,"guid").SetAttributeValue("uuid",Guid.NewGuid());Field(layer,"guid").SetAttributeValue("note",l.Name);Field(layer,"isVisible").Value=l.Visible?"true":"false";Field(layer,"opacity255").Value=l.Opacity.ToString();Refs(layer,"CLayeredImage",imageId);Refs(layer,"CImageResource",resourceId);
                var bounds=Field(layer,"boundsOnImageDoc");Field(bounds,"x").Value=l.Left.ToString();Field(bounds,"y").Value=l.Top.ToString();Field(bounds,"width").Value=l.Width.ToString();Field(bounds,"height").Value=l.Height.ToString();Field(layer,"layerName").Value=l.Name;Field(layer,"layerId").Value="PSD-"+l.Index;Field(layer,"layerIdValue_testImpl").Value=(l.Index+1).ToString();Attach(layer,layerId,"CLayer",l.Parent);layerList.Add(Ref("CLayer",layerId));
                string modelGuid=GuidDef("CModelImageGuid",l.Name);var m=new XElement(modelTemplate);Refs(m,"CModelImageGuid",modelGuid);Refs(m,"CLayeredImageGuid",imageGuid);Refs(m,"CLayer",layerId);Refs(m,"CImageResource",resourceId);Refs(m,"CModelImageGroup",modelGroupId);Field(m,"name").Value=l.Name;Field(m,"memo").Value="PSD source raster. Original atlas links retained; not automatically attached to a mesh.";
                var transform=Field(m,"_materialLocalToCanvasTransform");transform.SetAttributeValue("m02",l.Left);transform.SetAttributeValue("m12",l.Top);
                foreach(var size in m.Descendants("CSize")){size.SetAttributeValue("width",l.Width);size.SetAttributeValue("height",l.Height);}modelList.Add(m);modelImages[l.Index]=m;imported++;
                if(matchArtwork)
                {
                    var descriptor=PsdArtworkMatcher.Describe(png);
                    if(descriptor!=null)artworkCandidates.Add(new(l.Index,l.Name,descriptor));
                }
                var candidates=meshSources.Where(s=>Normalize((string)s.Descendants("CDrawableId").Single().Attribute("idstr")!)==Normalize(l.Name)||Normalize(Field(s,"localName").Value)==Normalize(l.Name)).ToArray();
                string status=unique[Normalize(l.Name)].Length>1?"Ambiguous duplicate PSD name":candidates.Length==1?"Unique name candidate (not pixel verified; texture link unchanged)":candidates.Length>1?"Ambiguous mesh names":"No mesh name match";
                if(unique[Normalize(l.Name)].Length==1&&candidates.Length==1){Field(candidates[0],"localName").Value=l.Name;matches++;}
                inventory.Add(new{l.Index,l.Name,l.Parent,l.Left,l.Top,l.Width,l.Height,l.Visible,l.Opacity,l.Blend,l.Clipping,File=file,Match="Name-only check: "+status,MeshIds=candidates.Select(s=>(string)s.Descendants("CDrawableId").Single().Attribute("idstr")!).ToArray(),l.Warnings});
                if(!matchArtwork)notes.Add($"PSD {l.Name}: {status}.");
            }
            catch(Exception e) when(e is InvalidDataException or OverflowException or ArgumentException)
            {notes.Add($"PSD LAYER NOT IMPORTED: {l.Name}: {e.Message}");inventory.Add(new{l.Index,l.Name,Error=e.Message});}
            progress?.Invoke(++completedLayers*65/Math.Max(1,totalLayers));
        }
        int artworkMatches=0,relinkedTextures=0;
        if(matchArtwork)
        {
            progress?.Invoke(68);
            update?.Invoke("Comparing PSD artwork with texture-atlas meshes…");
            using var model=JsonDocument.Parse(entries["moc2cmo.model.json"]);
            var results=PsdArtworkMatcher.Match(model.RootElement,textures??Array.Empty<string>(),artworkCandidates,notes);
            foreach(var result in results)
            {
                if(result.Status.StartsWith("Accepted",StringComparison.Ordinal))
                {
                    var mesh=meshSources.Single(s=>(string?)s.Descendants("CDrawableId").Single().Attribute("idstr")==result.MeshId);
                    Field(mesh,"localName").Value=result.LayerName!;
                    Field(modelImages[result.LayerIndex!.Value],"memo").Value+=" Artwork match (inferred): "+result.MeshId+".";
                    artworkMatches++;
                }
                notes.Add($"PSD ARTWORK: {result.MeshId} → {result.LayerName??"(none)"} [layer {result.LayerIndex?.ToString()??"-"}]: {result.Status}.");
            }
            File.WriteAllText(Path.Combine(exportDirectory,"artwork-matches.json"),JsonSerializer.Serialize(new{method="Premultiplied color/alpha thumbnail and aspect comparison inside mesh UV triangles; four rotations",maximumScore=0.025,minimumRunnerUpGap=0.012,maximumScoreRatio=0.65,scope="Inferred layer names and recorded mesh-to-layer associations only. Name-matching stage only: IDs, texture links, geometry and transforms unchanged by this stage. See texture-relinks.json for separately enabled source relinking. Scores are distances, not probabilities.",matches=results},new JsonSerializerOptions{WriteIndented=true}));
            if(relinkTextures)
            {
                update?.Invoke("Verifying experimental PSD texture-source alignment…");
                int started=0,total=results.Count(r=>r.Status.StartsWith("Accepted"));
                void LinkUpdate(string message){update?.Invoke(message);progress?.Invoke(78+(started++)*18/Math.Max(1,total));}
                var relinks=PsdTextureRelinker.Apply(doc,model.RootElement,results,modelImages,entries,psd.Layers,textures??Array.Empty<string>(),LinkUpdate);
                relinkedTextures=relinks.Count(r=>r.Status.StartsWith("Relinked"));
                foreach(var group in relinks.Where(r=>r.Status.StartsWith("Relinked")).GroupBy(r=>r.LayerIndex!.Value))Field(modelImages[group.Key],"memo").Value="PSD source raster. Experimental source links: "+string.Join(", ",group.Select(r=>r.MeshId))+". Active atlas retained; alignment inferred.";
                foreach(var r in relinks)notes.Add($"PSD TEXTURE RELINK: {r.MeshId}: {r.Status}");
                File.WriteAllText(Path.Combine(exportDirectory,"texture-relinks.json"),JsonSerializer.Serialize(new{experimental=true,scope="PSD source-image references and alignment transforms restored for accepted cases. Active atlas inputs, UVs and geometry retained, matching atlas-backed Editor project structure. This does not prove Cubism rendering equivalence.",maximumPixelRmse=0.035,maximumAlphaError=0.035,minimumVisibleValidationSamples=64,maximumCanvasFitError=1,results=relinks},new JsonSerializerOptions{WriteIndented=true}));
            }
            notes.Insert(0,$"PSD artwork-name matches accepted: {artworkMatches}. These are inferred associations; identical or weak candidates are not assigned.");
        }
        else notes.Insert(0,"PSD artwork matching disabled; existing name-only matching used.");
        Count(layerList);Count(modelList);
        var rawImages=doc.Descendants("carray_list").Single(e=>N(e)=="_rawImages");var wrapper=new XElement(rawImages.Elements().First());Refs(wrapper,"CLayeredImage",imageId);rawImages.Add(wrapper);Count(rawImages);
        var modelGroups=doc.Descendants("carray_list").Single(e=>N(e)=="_modelImageGroups");modelGroups.Add(Ref("CModelImageGroup",modelGroupId));Count(modelGroups);
        File.WriteAllText(Path.Combine(exportDirectory,"layers.json"),JsonSerializer.Serialize(new{psd.Width,psd.Height,Groups=psd.Layers.Where(l=>l.Section is 1 or 2).Select(l=>new{l.Index,l.Name,l.Parent,l.Visible,l.Opacity}),Layers=inventory},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(exportDirectory,"README.txt"),"PSD SOURCE ARTWORK\nOriginal layer rasters are embedded as source images in the CMO3 and exported as numbered PNGs here. layers.json contains original names, positions, visibility, group membership and matching candidates. Original PSD is preserved in the recovery ZIP.\n\nRig texture links stay on the recovered atlas. Unique name matches restore display labels only; they are not proof of correspondence. Unmatched artwork is available but no missing mesh or rig is invented. Masks/effects, non-normal blends and smart objects have limitations noted per layer. Raw PNG alpha excludes layer opacity and visibility, which remain metadata. When artwork matching is enabled, artwork-matches.json records accepted, ambiguous and rejected candidates. Accepted matches update display names and source-image memos only. When experimental texture relinking is selected, accepted PSD source-image links and inferred alignment transforms are restored; texture-relinks.json records results and skips. The active atlas remains in use, as in an atlas-backed original Editor project. Original placement transforms are not guaranteed. PSD groups remain source-image groups, not rig parts/deformers.\n");
        progress?.Invoke(97);
        ProjectEnricher.Validate(doc);using var ms=new MemoryStream();using(var writer=System.Xml.XmlWriter.Create(ms,new(){Encoding=new UTF8Encoding(false),Indent=true,CloseOutput=false}))doc.Save(writer);entries["main.xml"]=ms.ToArray();CaffArchive.Write(archivePath,entries);
        progress?.Invoke(100);
        return new(psd.Layers.Count(l=>l.Section==0),imported,matches,notes){ArtworkMatches=artworkMatches,RelinkedTextures=relinkedTextures};
    }
}
