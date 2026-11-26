using MocRecovery;
using System.Text.Json;

internal static class IndexedAtlasChecks
{
    public static void Run()
    {
        const int width = 19, height = 7;
        using var mesh = JsonDocument.Parse("""{"uvs":[[0.1,0],[0.92,0.25],[0.3,1]],"triangle_indices":[0,1,2]}""");
        foreach (int depth in new[] { 1, 2, 4, 8 })
        {
            byte[] indexed = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "indexed-fixtures", $"indexed-{depth}.png"));
            int colors = 1 << depth;
            var expected = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (x * 31 + y * 17) % colors;
                int offset = 4 * (y * width + x);
                expected[offset] = (byte)(i * 37);
                expected[offset + 1] = (byte)(i * 91 + 11);
                expected[offset + 2] = (byte)(255 - i);
                expected[offset + 3] = i == colors - 1 ? (byte)255 : (byte)(i * 67);
            }
            byte[] rgba = PsdReader.EncodePng(width, height, expected);
            PngRows.Read(indexed, (y, row) => {
                if (!row.AsSpan().SequenceEqual(expected.AsSpan(y * width * 4, width * 4)))
                    throw new Exception($"Indexed {depth}-bit row {y} differs from RGBA.");
            });
            (double, double)[] points = [(0.5, 0.5), (4.25, 3.75), (17.1, 5.9)];
            if (!PngRows.Sample(indexed, points).SelectMany(v => v).SequenceEqual(PngRows.Sample(rgba, points).SelectMany(v => v)))
                throw new Exception("PSD matching samples differ.");
            byte[] Crop(byte[] png)
            {
                using var pixels = new FileStream(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite,
                    FileShare.None, 4096, FileOptions.DeleteOnClose);
                PngRows.Read(png, (_, row) => pixels.Write(row));
                return RuntimeArtwork.Crop(mesh.RootElement, pixels, width, height)!.Value.Data;
            }
            if (!Crop(indexed).SequenceEqual(Crop(rgba))) throw new Exception("ArtMesh crop PNGs differ.");
            Console.WriteLine($"PASS: {depth}-bit indexed atlas matches RGBA rows, PSD samples and ArtMesh crop bytes.");
        }
    }
}
