using System.Xml.Linq;
using static MocRecovery.PsdTextureRelinker;

namespace MocRecovery;

public sealed record ArtworkRow(string Mesh, int Page, string Outcome, string? Source = null,
    int? X = null, int? Y = null, int? Width = null, int? Height = null,
    double? MaxOffset = null, string? Detail = null);

internal static class ArtworkPlacement
{
    // Compare the saved editable-image path with the extraction origin or accepted PSD fitting matrix.
    // For affine maps the largest displacement on a rectangle is attained at a corner.
    internal static double Error(double[] atlasToCanvas, double[] imageInput, double[] material,
        double[] expected, int width, int height)
    {
        var actual = Multiply(Inverse(atlasToCanvas), Multiply(imageInput, material));
        double worst = 0;
        foreach (var (x,y) in new[] { (0,0), (width,0), (0,height), (width,height) })
        {
            double dx = (actual[0]-expected[0])*x + (actual[1]-expected[1])*y + actual[2]-expected[2];
            double dy = (actual[3]-expected[3])*x + (actual[4]-expected[4])*y + actual[5]-expected[5];
            worst = Math.Max(worst, Math.Sqrt(dx*dx+dy*dy));
        }
        if (!double.IsFinite(worst)) throw new InvalidDataException("Non-finite source placement.");
        return worst;
    }

    internal static ArtworkRow Measure(ArtworkRow row, XElement image, XElement input, XElement atlas,
        double[] expected)
    {
        XElement Field(XElement e,string name) => e.Descendants().First(x => (string?)x.Attribute("xs.n")==name);
        try
        {
            var atlasToCanvas = Multiply(Matrix(Field(atlas,"optionalTransformOnCanvas")),
                Matrix(Field(atlas,"inputImageLocalToCanvasTransform")));
            return row with { MaxOffset = Error(atlasToCanvas, Matrix(Field(input,"optionalTransformOnCanvas")),
                Matrix(Field(image,"_materialLocalToCanvasTransform")), expected, row.Width!.Value, row.Height!.Value) };
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or FormatException or OverflowException)
        {
            return row with { Detail = row.Detail + " Placement unmeasured: " + e.Message };
        }
    }

    internal static (long Visible,long Mismatches,long Edge,long EdgeMismatches) CheckPixels(
        byte[] crop, FileStream decodedAtlas, int atlasWidth, int left, int top, CancellationToken token)
    {
        var (width,_) = PngRows.Size(crop);
        byte[] reference = new byte[width*4];
        long visible=0, mismatches=0, edge=0, edgeMismatches=0;
        PngRows.Read(crop,(y,row) =>
        {
            token.ThrowIfCancellationRequested();
            decodedAtlas.Position = 4L*((long)(top+y)*atlasWidth+left);
            decodedAtlas.ReadExactly(reference);
            for (int i=0;i<reference.Length;i+=4)
            {
                if (row[i+3]==0) continue;
                visible++;
                bool mismatch = !row.AsSpan(i,4).SequenceEqual(reference.AsSpan(i,4));
                if (mismatch) mismatches++;
                if (row[i+3]<255) { edge++; if (mismatch) edgeMismatches++; }
            }
        });
        return (visible,mismatches,edge,edgeMismatches);
    }
}
