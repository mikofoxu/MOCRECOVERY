using System.Text.Json;
using System.Xml.Linq;
namespace MocRecovery;
public sealed record ArtworkDescriptor(double[] Pixels,double Aspect)
{public double[]? Bounds {get;init;}}
public sealed record ArtworkCandidate(int LayerIndex,string LayerName,ArtworkDescriptor Descriptor);
public sealed record ArtworkMatch(string MeshId,int? LayerIndex,string? LayerName,double? Score,double? RunnerUpScore,int Rotation,string Status)
{public double[]? AtlasBounds {get;init;} public double[]? PsdBounds {get;init;}}
public static class PsdArtworkMatcher
{
    const int Preview=128,Size=32;
    public sealed class Accumulator
    {
        readonly int left,top,width,height;readonly int[] counts=new int[Preview*Preview];readonly double[] sums=new double[Preview*Preview*4];readonly bool[]? mask;
        public Accumulator(int left,int top,int width,int height,bool[]? mask=null){this.left=left;this.top=top;this.width=width;this.height=height;this.mask=mask;}
        public void Row(int y,byte[] row)
        {
            if(y<top||y>=top+height)return;int dy=(y-top)*Preview/height;
            for(int x=left;x<left+width;x++)
            {
                int cell=dy*Preview+(x-left)*Preview/width;counts[cell]++;
                if(mask!=null&&!mask[cell])continue;
                int p=x*4;if(row[p+3]==0)continue;double a=row[p+3]/255.0;int d=cell*4;sums[d]+=row[p]/255.0*a;sums[d+1]+=row[p+1]/255.0*a;sums[d+2]+=row[p+2]/255.0*a;sums[d+3]+=a;
            }
        }
        public ArtworkDescriptor? Finish()
        {
            for(int i=0;i<counts.Length;i++)if(counts[i]>0)for(int k=0;k<4;k++)sums[i*4+k]/=counts[i];
            // Fill unsampled bins for small images by their nearest source pixel's bin.
            for(int y=0;y<Preview;y++)for(int x=0;x<Preview;x++)if(counts[y*Preview+x]==0)
            {int xx=Math.Min(width-1,x*width/Preview)*Preview/width,yy=Math.Min(height-1,y*height/Preview)*Preview/height;for(int k=0;k<4;k++)sums[(y*Preview+x)*4+k]=sums[(yy*Preview+xx)*4+k];}
            int minX=Preview,minY=Preview,maxX=-1,maxY=-1;
            for(int y=0;y<Preview;y++)for(int x=0;x<Preview;x++)if(sums[(y*Preview+x)*4+3]>=16.0/255){minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
            if(maxX<minX)return null;double[] result=new double[Size*Size*4];
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
            {
                double px=minX+(x+.5)*(maxX-minX+1)/Size-.5,py=minY+(y+.5)*(maxY-minY+1)/Size-.5;
                int x0=Math.Clamp((int)Math.Floor(px),0,Preview-1),y0=Math.Clamp((int)Math.Floor(py),0,Preview-1),x1=Math.Min(x0+1,Preview-1),y1=Math.Min(y0+1,Preview-1);double fx=Math.Clamp(px-x0,0,1),fy=Math.Clamp(py-y0,0,1);
                for(int k=0;k<4;k++)result[(y*Size+x)*4+k]=(sums[(y0*Preview+x0)*4+k]*(1-fx)+sums[(y0*Preview+x1)*4+k]*fx)*(1-fy)+(sums[(y1*Preview+x0)*4+k]*(1-fx)+sums[(y1*Preview+x1)*4+k]*fx)*fy;
            }
            return new(result,(maxX-minX+1)*(double)width/((maxY-minY+1)*height)){Bounds=new[]{left+minX*width/(double)Preview,top+minY*height/(double)Preview,(maxX-minX+1)*width/(double)Preview,(maxY-minY+1)*height/(double)Preview}};
        }
    }
    public static ArtworkDescriptor? Describe(byte[] png)
    {var(w,h)=PngRows.Size(png);var a=new Accumulator(0,0,w,h);PngRows.Read(png,a.Row);return a.Finish();}
    public static ArtworkMatch Select(string meshId,ArtworkDescriptor? mesh,IReadOnlyList<ArtworkCandidate> candidates)
    {
        if(mesh==null)return new(meshId,null,null,null,null,0,"No visible artwork to match");
        var scores=new List<(ArtworkCandidate Candidate,double Score,int Rotation)>();
        foreach(var c in candidates)
        {
            double best=double.PositiveInfinity;int rotation=0;
            for(int k=0;k<4;k++)
            {
                double error=0;
                for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
                {var (xx,yy)=k switch{0=>(x,y),1=>(Size-1-y,x),2=>(Size-1-x,Size-1-y),_=>(y,Size-1-x)};for(int channel=0;channel<4;channel++)error+=Math.Abs(mesh.Pixels[(yy*Size+xx)*4+channel]-c.Descriptor.Pixels[(y*Size+x)*4+channel]);}
                error/=Size*Size*4;error+=.03*Math.Abs(Math.Log((k%2==0?mesh.Aspect:1/mesh.Aspect)/c.Descriptor.Aspect));
                if(error<best){best=error;rotation=k;}
            }
            scores.Add((c,best,rotation));
        }
        scores=scores.OrderBy(s=>s.Score).ThenBy(s=>s.Candidate.LayerIndex).ToList();
        if(scores.Count==0)return new(meshId,null,null,null,null,0,"No PSD artwork candidates");
        var first=scores[0];double? runner=scores.Count>1?scores[1].Score:null;
        bool close=first.Score<=.025,unique=runner==null||runner-first.Score>=.012&&first.Score<=runner*.65;
        string status=!close?"No sufficiently close artwork match":!unique?"Ambiguous artwork match":"Accepted artwork-name match (inferred)";
        return new(meshId,first.Candidate.LayerIndex,first.Candidate.LayerName,first.Score,runner,first.Rotation*90,status){AtlasBounds=mesh.Bounds,PsdBounds=first.Candidate.Descriptor.Bounds};
    }
    public static List<ArtworkMatch> Match(JsonElement model,IReadOnlyList<string> textures,IReadOnlyList<ArtworkCandidate> candidates,List<string> notes)
    {
        var results=new List<ArtworkMatch>();
        foreach(var page in model.GetProperty("art_meshes").EnumerateArray().GroupBy(m=>m.GetProperty("texture_index").GetInt32()))
        {
            try
            {
                if(page.Key<0||page.Key>=textures.Count||!File.Exists(textures[page.Key]))throw new InvalidDataException("Missing texture page "+page.Key);
                byte[] png=File.ReadAllBytes(textures[page.Key]);var(w,h)=PngRows.Size(png);var work=new List<(string Id,Accumulator Image)>();
                foreach(var mesh in page)
                {
                    string id=mesh.GetProperty("id").GetString()!;var uv=mesh.GetProperty("uvs").EnumerateArray().Select(v=>(X:v[0].GetDouble()*w,Y:v[1].GetDouble()*h)).ToArray();
                    if(uv.Length==0)continue;
                    int left=Math.Clamp((int)Math.Floor(uv.Min(v=>v.X)),0,w-1),top=Math.Clamp((int)Math.Floor(uv.Min(v=>v.Y)),0,h-1),right=Math.Clamp((int)Math.Ceiling(uv.Max(v=>v.X)),left+1,w),bottom=Math.Clamp((int)Math.Ceiling(uv.Max(v=>v.Y)),top+1,h);
                    int width=right-left,height=bottom-top;var mask=new bool[Preview*Preview];var indices=mesh.GetProperty("triangle_indices").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
                    for(int i=0;i+2<indices.Length;i+=3)
                    {
                        var a=uv[indices[i]];var b=uv[indices[i+1]];var c=uv[indices[i+2]];
                        double Cross((double X,double Y) p,(double X,double Y) q,double x,double y)=>(q.X-p.X)*(y-p.Y)-(q.Y-p.Y)*(x-p.X);
                        if(Math.Abs(Cross(a,b,c.X,c.Y))<1e-10)continue;
                        for(int y=0;y<Preview;y++)for(int x=0;x<Preview;x++)
                        {int pos=y*Preview+x;if(mask[pos])continue;double px=left+(x+.5)*width/Preview,py=top+(y+.5)*height/Preview;double ab=Cross(a,b,px,py),bc=Cross(b,c,px,py),ca=Cross(c,a,px,py);if(ab>=0&&bc>=0&&ca>=0||ab<=0&&bc<=0&&ca<=0)mask[pos]=true;}
                    }
                    work.Add((id,new Accumulator(left,top,width,height,mask)));
                }
                PngRows.Read(png,(y,row)=>{foreach(var item in work)item.Image.Row(y,row);});
                foreach(var item in work)results.Add(Select(item.Id,item.Image.Finish(),candidates));
            }
            catch(Exception e) when(e is InvalidDataException or IOException or ArgumentException or OverflowException or IndexOutOfRangeException)
            {notes.Add("PSD ARTWORK MATCHING SKIPPED: "+e.Message);foreach(var mesh in page)results.Add(new(mesh.GetProperty("id").GetString()!,null,null,null,null,0,"Texture page could not be analyzed"));}
        }
        return results;
    }
}
