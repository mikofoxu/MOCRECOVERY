using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace MocRecovery;

public sealed record TextureMeshRow(string Id, string Before, string WithinTolerance,
    string After, string WorstBefore, string WorstAfter, string? Reason = null);

public sealed record TextureReportData(string Summary, IReadOnlyList<TextureMeshRow> Rows)
{
    public const string Explanation = "Predicted for re-export using the same atlas layout. Each vertex passes if both UV coordinates are within 0.1 atlas pixel of the input MOC3. Offset is the larger absolute horizontal or vertical difference.";

    public string ToReportText()
    {
        var report = new StringBuilder("TEXTURE MAPPING ACCURACY").AppendLine().AppendLine();
        report.AppendLine(Explanation).AppendLine(Summary).AppendLine();
        report.AppendLine("ArtMesh  |  Before  |  Vertices within tolerance after correction  |  Worst before  |  Worst after");
        foreach (var row in Rows)
            report.AppendLine(row.Reason is null
                ? $"{row.Id}  |  {row.Before}  |  {row.WithinTolerance} ({row.After})  |  {row.WorstBefore} px  |  {row.WorstAfter} px"
                : $"{row.Id}  |  Unmeasured: {row.Reason}");
        return report.ToString();
    }
}

internal static class TextureAccuracy
{
    const double Tolerance = 0.1;
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    static XElement Field(XElement owner, string name) =>
        owner.Descendants().Single(e => (string?)e.Attribute("xs.n") == name);
    static float[] Values(XElement field) => field.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(s => float.Parse(s, Invariant)).ToArray();

    internal static TextureReportData Apply(XDocument doc, JsonElement model, IReadOnlyList<string> textures)
    {
        var objects = doc.Descendants().Where(e => e.Attribute("xs.id") != null)
            .ToDictionary(e => (string)e.Attribute("xs.id")!);
        XElement Resolve(XElement reference) => objects[(string)reference.Attribute("xs.ref")!];
        var meshes = doc.Descendants("CArtMeshSource").Where(e => e.HasElements)
            .ToDictionary(e => (string)e.Descendants("CDrawableId").Single().Attribute("idstr")!);
        var sizes = new Dictionary<int,(int Width,int Height)>();
        var lines = new List<(double Worst,TextureMeshRow Row)>();
        int total = 0, initialAccurate = 0, accurate = 0, skipped = 0;
        foreach (var mesh in model.GetProperty("art_meshes").EnumerateArray())
        {
            string id = mesh.GetProperty("id").GetString()!;
            int page = mesh.GetProperty("texture_index").GetInt32();
            try
            {
                if (!sizes.TryGetValue(page, out var size))
                {
                    if ((uint)page >= textures.Count || !File.Exists(textures[page]))
                        throw new InvalidDataException("atlas PNG unavailable");
                    using var file = File.OpenRead(textures[page]);
                    byte[] header = new byte[33];
                    file.ReadExactly(header);
                    sizes[page] = size = PngRows.Size(header);
                }
                var source = meshes[id];
                var extension = Resolve(source.Descendants("CTextureInputExtension").Single());
                var atlas = Resolve(Field(extension, "currentTextureInputData"));
                if (atlas.Name != "CTextureInput_TextureAtlasRegion") throw new InvalidDataException("active texture input is not an atlas");
                var map = PsdTextureRelinker.Multiply(PsdTextureRelinker.Matrix(Field(atlas,"optionalTransformOnCanvas")),
                    PsdTextureRelinker.Matrix(Field(atlas,"inputImageLocalToCanvasTransform")));
                var inverse = PsdTextureRelinker.Inverse(map);
                var positions = source.Elements("float-array").Single(e => (string?)e.Attribute("xs.n") == "positions");
                var points = Field(source.Descendants("GEditableMesh2").Single(), "point");
                var reference = Values(positions);
                var uvs = mesh.GetProperty("uvs");
                int n = uvs.GetArrayLength();
                if (n == 0 || reference.Length != 2*n || !reference.SequenceEqual(Values(points)))
                    throw new InvalidDataException("editable reference vertices could not be paired");
                var corrected = new float[reference.Length];
                double before = 0, after = 0;
                int initiallyGood = 0, good = 0;
                int i = 0;
                foreach (var uv in uvs.EnumerateArray())
                {
                    double u = uv[0].GetDouble()*size.Width, v = uv[1].GetDouble()*size.Height;
                    double Error(double x,double y) => Math.Max(Math.Abs(inverse[0]*x+inverse[1]*y+inverse[2]-u),
                        Math.Abs(inverse[3]*x+inverse[4]*y+inverse[5]-v));
                    double x = reference[2*i], y = reference[2*i+1];
                    double initial = Error(x,y);
                    corrected[2*i] = initial > Tolerance ? (float)(map[0]*u+map[1]*v+map[2]) : (float)x;
                    corrected[2*i+1] = initial > Tolerance ? (float)(map[3]*u+map[4]*v+map[5]) : (float)y;
                    double final = Error(corrected[2*i],corrected[2*i+1]);
                    if (!double.IsFinite(final) || !double.IsFinite(initial) || final > initial+1e-8)
                        throw new InvalidDataException("unstable texture transform");
                    before = Math.Max(before,initial);
                    after = Math.Max(after,final);
                    if (initial <= Tolerance) initiallyGood++;
                    if (final <= Tolerance) good++;
                    i++;
                }
                if (before > Tolerance)
                {
                    string values = string.Join(" ",corrected.Select(f => f.ToString("R",Invariant)));
                    positions.Value = values;
                    points.Value = values;
                }
                lines.Add((before, new TextureMeshRow(id, (100.0*initiallyGood/n).ToString("F2",Invariant)+"%",
                    FormattableString.Invariant($"{good}/{n}"), (100.0*good/n).ToString("F2",Invariant)+"%",
                    before.ToString("F4",Invariant), after.ToString("F4",Invariant))));
                total += n;
                initialAccurate += initiallyGood;
                accurate += good;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or KeyNotFoundException or InvalidOperationException or FormatException or UnauthorizedAccessException)
            {
                skipped++;
                lines.Add((-1,new TextureMeshRow(id,"—","—","—","—","—",e.Message)));
            }
        }
        string overall = total == 0 ? "Overall: unmeasured." : FormattableString.Invariant($"Overall: {100.0*initialAccurate/total:F2}% before correction; {accurate}/{total} measured vertices ({100.0*accurate/total:F2}%) within tolerance.");
        return new TextureReportData(overall + Environment.NewLine + $"Unmeasured meshes: {skipped}.",
            lines.OrderByDescending(row => row.Worst).Select(row => row.Row).ToArray());
    }
}
