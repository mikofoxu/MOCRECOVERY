using System.Text.Json;
using System.Xml.Linq;

namespace MocRecovery;

internal static class PartLayerOrder
{
    internal static string Restore(XDocument doc, JsonElement model, MocIntTables tables)
    {
        var meshes = model.GetProperty("art_meshes");
        var ranks = new Dictionary<string,(int Group,int Rank)>();
        int groups = tables.At(0,18), entries = tables.At(0,19);
        for (int group = 0; group < groups; group++)
        {
            int start = tables.At(81,group), count = tables.At(82,group);
            if (count < 0 || count > 0 && (start < 0 || start > entries - (long)count))
                throw new InvalidDataException("Invalid draw-order group range.");
            for (int offset = 0; offset < count; offset++)
            {
                int entry = start + offset;
                if (tables.At(86,entry) != 0) continue;
                int index = tables.At(87,entry);
                if ((uint)index >= meshes.GetArrayLength())
                    throw new InvalidDataException("Invalid ArtMesh in draw-order group.");
                string id = meshes[index].GetProperty("id").GetString()!;
                if (!ranks.TryAdd(id,(group,offset)))
                    throw new InvalidDataException("Repeated ArtMesh in draw-order groups: " + id);
            }
        }

        var byGuid = new Dictionary<string,(int Group,int Rank)>();
        var sharedIds = doc.Descendants("CDrawableId").Where(e => e.Attribute("xs.id") != null)
            .ToDictionary(e => (string)e.Attribute("xs.id")!, e => (string)e.Attribute("idstr")!);
        foreach (var mesh in doc.Descendants("CArtMeshSource").Where(e => e.HasElements))
        {
            var drawable = mesh.Descendants("CDrawableId").Single(e => (string?)e.Attribute("xs.n") == "id");
            string id = (string?)drawable.Attribute("idstr") ?? sharedIds[(string)drawable.Attribute("xs.ref")!];
            if (ranks.TryGetValue(id,out var rank))
                byGuid.Add((string)mesh.Descendants("CDrawableGuid").Single(e => (string?)e.Attribute("xs.n") == "guid").Attribute("xs.ref")!,rank);
        }
        int changed = 0;
        foreach (var part in doc.Descendants("CPartSource").Where(e => e.HasElements))
        {
            var children = part.Elements().Single(e => (string?)e.Attribute("xs.n") == "_childGuids");
            var slots = children.Elements("CDrawableGuid").Select(e => e.Attribute("xs.ref")!)
                .Where(a => byGuid.ContainsKey(a.Value)).GroupBy(a => byGuid[a.Value].Group);
            bool different = false;
            foreach (var group in slots)
            {
                // Runtime draws back to front. Cubism lists the frontmost sibling first.
                var ordered = group.Select(a => a.Value).OrderByDescending(id => byGuid[id].Rank).ToArray();
                int i = 0;
                foreach (var slot in group)
                {
                    different |= slot.Value != ordered[i];
                    slot.Value = ordered[i++];
                }
            }
            if (different) changed++;
        }
        return $"Part stacking checked against runtime draw-order groups: {ranks.Count} ArtMeshes; reordered {changed} Parts.";
    }
}
