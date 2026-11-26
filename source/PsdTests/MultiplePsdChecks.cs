using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using MocRecovery;

internal static class MultiplePsdChecks
{
    internal static async Task Run(string moc,string engine,string folder)
    {
        var plan=Recovery.Inspect(Path.GetFullPath(moc),Path.GetFullPath(folder));
        plan.MatchPsdArtwork=true;plan.RelinkPsdTextures=true;
        Directory.CreateDirectory(folder);
        string fixture=Path.Combine(AppContext.BaseDirectory,"grouped.psd");
        foreach(string directory in new[]{"first","second"})
        {
            string path=Path.Combine(folder,directory,"Artwork.psd");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(fixture,path,true);
            plan.PsdInputs.Add(path);
        }
        plan.PsdInputs.Add(plan.PsdInputs[0]);
        await Recovery.ConvertAsync(plan,Path.GetFullPath(engine),true);
        var entries=CaffArchive.Read(plan.Output);
        var xml=XDocument.Load(new MemoryStream(entries["main.xml"]));
        ProjectEnricher.Validate(xml);
        var groups=xml.Descendants("s").Where(e=>(string?)e.Attribute("xs.n")=="groupName").Select(e=>e.Value).ToHashSet();
        using var zip=ZipFile.OpenRead(plan.Backup);
        for(int i=1;i<=2;i++)
        {
            string prefix=$"editor-import/psd-source/{i:D3}/";
            if(!groups.Contains($"PSD {i}: Artwork.psd"))throw new Exception("Missing PSD group.");
            using var inventory=JsonDocument.Parse(zip.GetEntry(prefix+"layers.json")!.Open());
            foreach(var layer in inventory.RootElement.GetProperty("Layers").EnumerateArray())
            {
                string file=layer.GetProperty("File").GetString()!;
                using var stream=zip.GetEntry(prefix+file)!.Open();using var copy=new MemoryStream();stream.CopyTo(copy);
                if(!entries[file].AsSpan().SequenceEqual(copy.ToArray())||!file.StartsWith($"psd-layer-{i:D3}-"))
                    throw new Exception("PSD layer bytes or image resource name differs.");
            }
            using var original=zip.GetEntry($"original/source-artwork/{i:D3}/Artwork.psd")!.Open();
            using var saved=new MemoryStream();original.CopyTo(saved);
            if(!File.ReadAllBytes(fixture).AsSpan().SequenceEqual(saved.ToArray()))throw new Exception("Original PSD backup differs.");
        }
        if(zip.Entries.Count(e=>e.FullName.EndsWith(".psd"))!=2)throw new Exception("Repeated input backed up twice.");
        Console.WriteLine("PASS: sequential multi-PSD conversion, same-name documents, unique layer resources, original files and inventories.");
    }
}
