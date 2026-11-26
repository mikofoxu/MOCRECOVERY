using System.Text.Json;
namespace MocRecovery;

// Scan one document at a time. Only small comparison thumbnails survive the scan.
internal sealed class PsdBatchMatches
{
    internal Dictionary<string,int> NameCounts {get;}=new();
    internal HashSet<string> ArtworkOwners {get;}=new();
    internal List<string> Notes {get;}=[];
    internal List<ArtworkMatch>[] Results {get;}
    PsdBatchMatches(int count)=>Results=Enumerable.Range(0,count).Select(_=>new List<ArtworkMatch>()).ToArray();

    internal static PsdBatchMatches Scan(string[] files,bool match,JsonElement model,IReadOnlyList<string> textures,Action<string> stage)
    {
        var batch=new PsdBatchMatches(files.Length);
        var candidates=new List<ArtworkCandidate>();
        var locations=new List<(int File,int Layer)>();
        for(int i=0;i<files.Length;i++)
        {
            string label=$"Checking PSD {i+1}/{files.Length} ({Path.GetFileName(files[i])})";
            stage(label);
            using var reader=new PsdReader(files[i]);
            foreach(var layer in reader.Layers.Where(l=>l.Section==0))
            {
                string name=PsdImporter.Normalize(layer.Name);
                batch.NameCounts[name]=batch.NameCounts.GetValueOrDefault(name)+1;
                if(!match)continue;
                stage(label+" — "+layer.Name);
                try
                {
                    var thumbnail=PsdArtworkMatcher.Describe(reader.Png(layer));
                    if(thumbnail==null)continue;
                    candidates.Add(new(locations.Count,layer.Name,thumbnail));
                    locations.Add((i,layer.Index));
                }
                catch(Exception e) when(e is InvalidDataException or OverflowException or ArgumentException)
                {batch.Notes.Add($"{label}: artwork comparison skipped for {layer.Name}: {e.Message}");}
            }
        }
        if(!match)return batch;
        stage("Comparing PSD artwork candidates across all selected files…");
        foreach(var result in PsdArtworkMatcher.Match(model,textures,candidates,batch.Notes))
        {
            if(result.LayerIndex is not int index){batch.Notes.Add($"{result.MeshId}: {result.Status}");continue;}
            var location=locations[index];
            batch.Results[location.File].Add(result with {LayerIndex=location.Layer});
            if(result.Status.StartsWith("Accepted",StringComparison.Ordinal))batch.ArtworkOwners.Add(result.MeshId);
        }
        return batch;
    }
}
