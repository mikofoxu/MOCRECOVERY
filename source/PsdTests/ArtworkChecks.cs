using MocRecovery;
public static class ArtworkChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    public static void Run()
    {
        byte[] rgba=new byte[32*32*4];for(int y=0;y<32;y++)for(int x=0;x<32;x++){int p=(y*32+x)*4;rgba[p]=(byte)(x*7);rgba[p+1]=(byte)(y*7);rgba[p+2]=90;rgba[p+3]=(byte)(x<26&&y<20?255:0);}
        var png=PsdReader.EncodePng(32,32,rgba);var decoded=new byte[rgba.Length];PngRows.Read(png,(y,row)=>row.CopyTo(decoded,y*32*4));Check(decoded.SequenceEqual(rgba),"matching PNG decoder preserves RGBA");
        var descriptor=PsdArtworkMatcher.Describe(png)!;var candidate=new ArtworkCandidate(7,"original layer",descriptor);
        var accepted=PsdArtworkMatcher.Select("UnrelatedId",descriptor,new[]{candidate});
        Check(accepted.Status.StartsWith("Accepted")&&accepted.LayerIndex==7&&accepted.MeshId=="UnrelatedId","artwork matching works independently of ID/name");
        var ambiguous=PsdArtworkMatcher.Select("mesh",descriptor,new[]{candidate,new ArtworkCandidate(8,"duplicate",descriptor)});
        Check(ambiguous.Status.StartsWith("Ambiguous"),"identical artwork is not assigned to an arbitrary PSD layer");
        var other=new ArtworkDescriptor(Enumerable.Repeat(1.0,32*32*4).ToArray(),1);
        Check(!PsdArtworkMatcher.Select("mesh",other,new[]{candidate}).Status.StartsWith("Accepted"),"unrelated artwork rejected");
        Check(PsdArtworkMatcher.Describe(PsdReader.EncodePng(32,32,new byte[rgba.Length]))==null,"fully transparent layer excluded");
        var rotated=new double[descriptor.Pixels.Length];
        for(int y=0;y<32;y++)for(int x=0;x<32;x++)for(int k=0;k<4;k++)rotated[((31-x)*32+y)*4+k]=descriptor.Pixels[(y*32+x)*4+k];
        Check(PsdArtworkMatcher.Select("rotated",new(rotated,1/descriptor.Aspect),new[]{candidate}).Status.StartsWith("Accepted"),"quarter-turn atlas rotation accepted");
        byte[] small=PsdReader.EncodePng(1,1,new byte[]{255,0,0,255});Check(PsdArtworkMatcher.Describe(small)!.Pixels.All(double.IsFinite),"tiny layers produce finite descriptors");
        Check(new Plan{Input="test",Output="test"}.MatchPsdArtwork,"PSD matching defaults on in recovery plan");
    }
}
