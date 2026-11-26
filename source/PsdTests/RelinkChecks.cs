using MocRecovery;
using System.Text.Json;
using System.Xml.Linq;
public static class RelinkChecks
{
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
    public static void Run()
    {
        var samplePng=PsdReader.EncodePng(2,2,new byte[]{255,0,0,255,0,255,0,255,0,0,255,255,255,255,255,255});
        var samples=PngRows.Sample(samplePng,new[]{(1.0,1.0),(-2.0,-2.0)});
        Check(samples[0].Take(3).All(v=>Math.Abs(v-.5)<1e-12)&&samples[0][3]==1&&samples[1].All(v=>v==0),"full-resolution PNG sampling interpolates pixels and treats out-of-bounds as transparent");
        using var model=JsonDocument.Parse("""{"art_meshes":[{"id":"mesh","texture_index":0,"uvs":[[0.25,0.25],[0.5,0.25],[0.5,0.5],[0.25,0.5]],"triangle_indices":[0,1,2,0,2,3]}]}""");
        var xml=XDocument.Parse("""<root><CArtMeshSource xs.id="#mesh"><CDrawableId idstr="mesh"/><CTextureInputExtension xs.ref="#ext"/><float-array xs.n="positions">16 16 32 16 32 32 16 32</float-array><float-array xs.n="uvs">0.25 0.25 0.5 0.25 0.5 0.5 0.25 0.5</float-array><TextureState xs.n="textureState" v="TEXTURE_ATLAS"/></CArtMeshSource><CTextureInputExtension xs.id="#ext"><carray_list xs.n="_textureInputs"><CTextureInput_ModelImage xs.ref="#input"/><CTextureInput_TextureAtlasRegion xs.ref="#atlas"/></carray_list><CTextureInput_TextureAtlasRegion xs.n="currentTextureInputData" xs.ref="#atlas"/></CTextureInputExtension><CTextureInput_ModelImage xs.id="#input"><CAffine xs.n="optionalTransformOnCanvas" m00="1" m01="0" m02="0" m10="0" m11="1" m12="0"/><CModelImageGuid xs.n="_modelImageGuid" xs.ref="#old"/></CTextureInput_ModelImage><CTextureInput_TextureAtlasRegion xs.id="#atlas"><CAffine xs.n="optionalTransformOnCanvas" m00="1" m01="0" m02="0" m10="0" m11="1" m12="0"/><CAffine xs.n="inputImageLocalToCanvasTransform" m00="1" m01="0" m02="0" m10="0" m11="1" m12="0"/></CTextureInput_TextureAtlasRegion><CModelImage><CModelImageGuid xs.n="guid" xs.ref="#new"/><CAffine xs.n="_materialLocalToCanvasTransform" m00="1" m01="0" m02="100" m10="0" m11="1" m12="200"/></CModelImage></root>""");
        byte[] source=new byte[16*16*4];for(int y=0;y<16;y++)for(int x=0;x<16;x++){int p=(y*16+x)*4;source[p]=(byte)(x*15);source[p+1]=(byte)(y*15);source[p+2]=(byte)((x+y)*7);source[p+3]=255;}
        var entries=new Dictionary<string,byte[]>{{"psd-layer-0000.png",PsdReader.EncodePng(16,16,source)}};var layer=new PsdLayer{Index=0,Name="source",Width=16,Height=16,Left=100,Top=200,Opacity=255,Blend="norm"};
        string folder=Path.Combine(Path.GetTempPath(),"relink-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string file=Path.Combine(folder,"atlas.png");
        try
        {
            foreach(int rotation in new[]{0,90,180,270})
            {
                byte[] atlas=new byte[64*64*4];var matrix=PsdTextureRelinker.Alignment([0,0,16,16],[16,16,16,16],rotation);
                for(int y=0;y<16;y++)for(int x=0;x<16;x++){double px=matrix[0]*(x+.5)+matrix[1]*(y+.5)+matrix[2],py=matrix[3]*(x+.5)+matrix[4]*(y+.5)+matrix[5];Array.Copy(source,(y*16+x)*4,atlas,((int)py*64+(int)px)*4,4);}
                File.WriteAllBytes(file,PsdReader.EncodePng(64,64,atlas));var doc=new XDocument(xml);string before=doc.Descendants("CArtMeshSource").Single().ToString();
                var match=new ArtworkMatch("mesh",0,"source",0,1,rotation,"Accepted"){PsdBounds=[0,0,16,16],AtlasBounds=[16,16,16,16]};
                List<RelinkResult> Apply(XDocument d)=>PsdTextureRelinker.Apply(d,model.RootElement,[match],new(){{0,d.Descendants("CModelImage").Single()}},entries,[layer],[file]);
                var result=Apply(doc).Single();Check(result.Status.StartsWith("Relinked"),$"relink accepts verified {rotation}-degree alignment");
                Check(doc.Descendants("CArtMeshSource").Single().ToString()==before&&doc.Descendants().Single(e=>(string?)e.Attribute("xs.n")=="currentTextureInputData").Attribute("xs.ref")!.Value=="#atlas","relink preserves mesh geometry, UVs and active atlas");
                Check(doc.Descendants().Single(e=>(string?)e.Attribute("xs.n")=="_modelImageGuid").Attribute("xs.ref")!.Value=="#new","relink changes source-image reference");
                var composed=PsdTextureRelinker.Multiply(result.OptionalTransform!,[1,0,100,0,1,200]);Check(composed.Zip(matrix).All(p=>Math.Abs(p.First-p.Second)<1e-6),"source transform composition cancels PSD canvas offset");
                var damaged=new XDocument(xml);damaged.Descendants("float-array").First().Value="116 16 32 16 32 32 16 32";string snapshot=damaged.ToString();Check(Apply(damaged).Single().Status.Contains("canvas")&&damaged.ToString()==snapshot,"non-affine geometry rejects relink without mutation");
                layer.Warnings.Add("mask unsupported");var effects=new XDocument(xml);snapshot=effects.ToString();Check(Apply(effects).Single().Status.Contains("manual review")&&effects.ToString()==snapshot,"unsupported layer effects reject without mutation");layer.Warnings.Clear();
                Array.Fill(atlas,(byte)255);File.WriteAllBytes(file,PsdReader.EncodePng(64,64,atlas));var wrong=new XDocument(xml);snapshot=wrong.ToString();Check(!Apply(wrong).Single().Status.StartsWith("Relinked")&&wrong.ToString()==snapshot,"unrelated atlas fails alignment without mutation");
            }
            Check(!new Plan{Input="a",Output="b"}.RelinkPsdTextures,"experimental relinking defaults off");
        }
        finally{Directory.Delete(folder,true);}
    }
}
