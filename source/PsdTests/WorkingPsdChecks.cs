using System.Text.Json;
using System.Xml.Linq;
using MocRecovery;

internal static class WorkingPsdChecks
{
    internal static void Run()
    {
        string directory=Directory.CreateTempSubdirectory().FullName;
        try
        {
            string page=Path.Combine(directory,"page.png"),psd=Path.Combine(directory,"model.working.psd");
            File.WriteAllBytes(page,PsdReader.EncodePng(2,2,[255,0,0,255, 0,255,0,255, 0,0,255,255, 255,255,0,128]));
            var project=XDocument.Parse("""
                <root><CImageCanvas><i xs.n="pixelWidth">8</i><i xs.n="pixelHeight">8</i></CImageCanvas>
                <CPartSource><CPartId idstr="__RootPart__"/><CPartGuid xs.n="guid" xs.ref="#1"/><list xs.n="_childGuids"><CPartGuid xs.ref="#2"/></list></CPartSource>
                <CPartSource><CPartId idstr="Face"/><CPartGuid xs.n="guid" xs.ref="#2"/><s xs.n="localName">Face</s><list xs.n="_childGuids"><CDrawableGuid xs.ref="#3"/></list></CPartSource>
                <CArtMeshSource><CDrawableId xs.n="id" idstr="Eye"/><CDrawableGuid xs.n="guid" xs.ref="#3"/><s xs.n="localName">Eye 目</s>
                <float-array xs.n="positions">3 2 5 2 5 4 3 4</float-array><float-array xs.n="uvs">0 1 0 0 1 0 1 1</float-array></CArtMeshSource></root>
                """);
            using var model=JsonDocument.Parse("""{"parameters":[],"binding_bands":[],"art_meshes":[{"id":"Eye","texture_index":0,"binding_band_index":null,"triangle_indices":[0,1,2,0,2,3],"keyforms":[{"opacity":1,"draw_order":500}]}]}""");
            LayeredPsd.Build(psd,project,model.RootElement,[page],_=>{},default);
            using var reader=new PsdReader(psd);
            var layer=reader.Layers.Single(l=>l.Name=="Eye 目");
            if(reader.Width!=8||reader.Height!=8||layer.Left!=3||layer.Top!=2||layer.Width!=2||layer.Height!=2||
                layer.Parent!=reader.Layers.Single(l=>l.Name=="Face").Index)throw new Exception("Incorrect PSD placement or Part.");
            var pixels=new List<byte>();PngRows.Read(reader.Png(layer),(_,row)=>pixels.AddRange(row));
            byte[] expected=[0,0,255,255,255,0,0,255,255,255,0,128,0,255,0,255];
            if(!pixels.SequenceEqual(expected))throw new Exception("Rotated mesh colors or alpha differ.");
            Console.WriteLine("PASS: working PSD rotated texture, alpha, canvas offset, Part and Unicode name");
        }
        finally {Directory.Delete(directory,true);}
    }
}
