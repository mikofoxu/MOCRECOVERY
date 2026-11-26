using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
namespace MocRecovery;
public sealed record RelinkResult(string MeshId,int? LayerIndex,string Status,double? PixelRmse=null,double? AlphaError=null,double? GeometryError=null,double[]? SourceToAtlas=null,double[]? OptionalTransform=null);
public static class PsdTextureRelinker
{
    static string? Name(XElement e)=>(string?)e.Attribute("xs.n");
    static XElement Field(XElement e,string n)=>e.Descendants().Single(x=>Name(x)==n);
    static double D(string s)=>double.Parse(s,CultureInfo.InvariantCulture);
    // [m00,m01,m02,m10,m11,m12], column-vector convention.
    public static double[] Multiply(double[] a,double[] b)=>[a[0]*b[0]+a[1]*b[3],a[0]*b[1]+a[1]*b[4],a[0]*b[2]+a[1]*b[5]+a[2],a[3]*b[0]+a[4]*b[3],a[3]*b[1]+a[4]*b[4],a[3]*b[2]+a[4]*b[5]+a[5]];
    public static double[] Inverse(double[] a){double d=a[0]*a[4]-a[1]*a[3];if(!double.IsFinite(d)||Math.Abs(d)<1e-12)throw new InvalidDataException("Singular image transform.");return [a[4]/d,-a[1]/d,(a[1]*a[5]-a[4]*a[2])/d,-a[3]/d,a[0]/d,(a[3]*a[2]-a[0]*a[5])/d];}
    static (double X,double Y) Point(double[] a,double x,double y)=>(a[0]*x+a[1]*y+a[2],a[3]*x+a[4]*y+a[5]);
    static readonly string[] Fields=["m00","m01","m02","m10","m11","m12"];
    static double[] Matrix(XElement e)=>Fields.Select(n=>D((string)e.Attribute(n)!)).ToArray();
    public static double[] Alignment(double[] source,double[] atlas,int rotation)
    {
        double[] normalize=[1/source[2],0,-source[0]/source[2],0,1/source[3],-source[1]/source[3]];
        double[] rotate=rotation switch{0=>[1,0,0,0,1,0],90=>[0,-1,1,1,0,0],180=>[-1,0,1,0,-1,1],270=>[0,1,0,-1,0,1],_=>throw new InvalidDataException("Unsupported rotation.")};
        return Multiply([atlas[2],0,atlas[0],0,atlas[3],atlas[1]],Multiply(rotate,normalize));
    }
    sealed class Probe
    {
        readonly int left,top,width,height,w,h;readonly float[] data;
        public Probe(int imageWidth,int imageHeight,double[] bounds)
        {
            left=Math.Clamp((int)Math.Floor(bounds[0]),0,imageWidth-1);top=Math.Clamp((int)Math.Floor(bounds[1]),0,imageHeight-1);
            width=Math.Clamp((int)Math.Ceiling(bounds[0]+bounds[2]),left+1,imageWidth)-left;height=Math.Clamp((int)Math.Ceiling(bounds[1]+bounds[3]),top+1,imageHeight)-top;
            w=Math.Min(512,width);h=Math.Min(512,height);data=new float[w*h*4];
        }
        public void Row(int y,byte[] row)
        {
            if(y<top||y>=top+height)return;
            int start=Math.Max(0,(int)Math.Ceiling((y-top)*h/(double)height-.5)),end=Math.Min(h-1,(int)Math.Ceiling((y-top+1)*h/(double)height-.5)-1);
            for(int yy=start;yy<=end;yy++)for(int x=0;x<w;x++)
            {int xx=left+Math.Min(width-1,(int)((x+.5)*width/w));int s=xx*4,d=(yy*w+x)*4;float a=row[s+3]/255f;data[d]=row[s]/255f*a;data[d+1]=row[s+1]/255f*a;data[d+2]=row[s+2]/255f*a;data[d+3]=a;}
        }
        public double[] Sample(double x,double y)
        {
            x=(x-left)*w/width-.5;y=(y-top)*h/height-.5;var r=new double[4];int x0=(int)Math.Floor(x),y0=(int)Math.Floor(y);double fx=x-x0,fy=y-y0;
            for(int yy=0;yy<2;yy++)for(int xx=0;xx<2;xx++){int px=x0+xx,py=y0+yy;if(px<0||py<0||px>=w||py>=h)continue;double weight=(xx==0?1-fx:fx)*(yy==0?1-fy:fy);for(int c=0;c<4;c++)r[c]+=data[(py*w+px)*4+c]*weight;}
            return r;
        }
    }
    sealed record PixelTest(double X,double Y,double[] Value);
    static List<PixelTest> Tests(JsonElement mesh,int width,int height,Probe atlas,int side,double offset)
    {
        var uv=mesh.GetProperty("uvs").EnumerateArray().Select(v=>(X:v[0].GetDouble()*width,Y:v[1].GetDouble()*height)).ToArray();var indices=mesh.GetProperty("triangle_indices").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
        double left=uv.Min(v=>v.X),top=uv.Min(v=>v.Y),w=uv.Max(v=>v.X)-left,h=uv.Max(v=>v.Y)-top;var result=new List<PixelTest>();
        for(int y=0;y<side;y++)for(int x=0;x<side;x++)
        {
            double px=left+(x+offset)*w/side,py=top+(y+offset)*h/side;bool inside=false;
            for(int i=0;i+2<indices.Length&&!inside;i+=3)
            {
                var a=uv[indices[i]];var b=uv[indices[i+1]];var c=uv[indices[i+2]];
                double Cross((double X,double Y) p,(double X,double Y) q,double xx,double yy)=>(q.X-p.X)*(yy-p.Y)-(q.Y-p.Y)*(xx-p.X);
                if(Math.Abs(Cross(a,b,c.X,c.Y))<1e-9)continue;double ab=Cross(a,b,px,py),bc=Cross(b,c,px,py),ca=Cross(c,a,px,py);inside=ab>=0&&bc>=0&&ca>=0||ab<=0&&bc<=0&&ca<=0;
            }
            if(inside)result.Add(new(px,py,atlas.Sample(px,py)));
        }
        return result;
    }
    static (double Rmse,double Alpha,int Visible) Error(Probe source,double[] matrix,List<PixelTest> tests)
    {
        var inverse=Inverse(matrix);double sum=0,alpha=0;int count=0,visible=0;
        foreach(var t in tests)
        {var p=Point(inverse,t.X,t.Y);var s=source.Sample(p.X,p.Y);if(Math.Max(s[3],t.Value[3])<.03)continue;visible++;for(int c=0;c<4;c++){double e=s[c]-t.Value[c];sum+=e*e;count++;}alpha+=Math.Abs(s[3]-t.Value[3]);}
        return count==0?(1,1,0):(Math.Sqrt(sum/count),alpha/visible,visible);
    }
    public static List<RelinkResult> Apply(XDocument doc,JsonElement model,List<ArtworkMatch> matches,Dictionary<int,XElement> modelImages,Dictionary<string,byte[]> entries,IReadOnlyList<PsdLayer> layers,IReadOnlyList<string> textures,Action<string>? update=null,string prefix="")
    {
        var results=new List<RelinkResult>();var objects=doc.Descendants().Where(e=>e.Attribute("xs.id")!=null).ToDictionary(e=>(string)e.Attribute("xs.id")!);
        var sources=doc.Descendants("CArtMeshSource").Where(e=>e.HasElements).ToDictionary(e=>(string)e.Descendants("CDrawableId").Single().Attribute("idstr")!);
        foreach(var rejected in matches.Where(m=>!m.Status.StartsWith("Accepted")))results.Add(new(rejected.MeshId,rejected.LayerIndex,"Not relinked: no unique accepted artwork match."));
        foreach(var page in model.GetProperty("art_meshes").EnumerateArray().GroupBy(m=>m.GetProperty("texture_index").GetInt32()))
        {
            var candidates=page.Select(mesh=>(Mesh:mesh,Match:matches.SingleOrDefault(m=>m.MeshId==mesh.GetProperty("id").GetString()&&m.Status.StartsWith("Accepted")))).Where(p=>p.Match!=null).ToArray();if(candidates.Length==0)continue;
            try
            {
                byte[] atlasPng=File.ReadAllBytes(textures[page.Key]);var(width,height)=PngRows.Size(atlasPng);var probes=new Dictionary<string,Probe>();
                foreach(var (mesh,match) in candidates)
                {
                    var uv=mesh.GetProperty("uvs").EnumerateArray().ToArray();double left=uv.Min(v=>v[0].GetDouble())*width,top=uv.Min(v=>v[1].GetDouble())*height;
                    probes[match!.MeshId]=new(width,height,[left-3,top-3,uv.Max(v=>v[0].GetDouble())*width-left+6,uv.Max(v=>v[1].GetDouble())*height-top+6]);
                }
                PngRows.Read(atlasPng,(y,row)=>{foreach(var p in probes.Values)p.Row(y,row);});
                var validationSets=candidates.ToDictionary(c=>c.Match!.MeshId,c=>Tests(c.Mesh,width,height,probes[c.Match!.MeshId],33,.75));
                var requests=validationSets.SelectMany(p=>p.Value.Select((v,i)=>(Id:p.Key,Index:i,Point:(v.X,v.Y)))).ToArray();
                var exactAtlas=PngRows.Sample(atlasPng,requests.Select(r=>r.Point).ToArray());
                for(int i=0;i<requests.Length;i++){var r=requests[i];var old=validationSets[r.Id][r.Index];validationSets[r.Id][r.Index]=old with{Value=exactAtlas[i]};}
                foreach(var (mesh,nullableMatch) in candidates)
                {
                    var match=nullableMatch!;string id=match.MeshId;update?.Invoke("Checking experimental PSD texture link: "+id+"…");
                    try
                    {
                        var layer=layers.Single(l=>l.Index==match.LayerIndex);if(layer.Opacity!=255||layer.Blend!="norm"||layer.Clipping||layer.Warnings.Count>0)throw new InvalidDataException("PSD opacity, masks, blending or effects require manual review.");
                        if(match.PsdBounds==null||match.AtlasBounds==null)throw new InvalidDataException("Alignment bounds unavailable.");
                        var bounds=match.PsdBounds;byte[] png=entries[$"psd-layer-{prefix}{layer.Index:D4}.png"];var(sw,sh)=PngRows.Size(png);var source=new Probe(sw,sh,[bounds[0]-8,bounds[1]-8,bounds[2]+16,bounds[3]+16]);PngRows.Read(png,source.Row);
                        var train=Tests(mesh,width,height,probes[id],16,.25);var validation=validationSets[id];
                        var fitted=(double[])match.AtlasBounds.Clone();double best=Error(source,Alignment(bounds,fitted,match.Rotation),train).Rmse;
                        double start=Math.Max(4,Math.Max(fitted[2],fitted[3])/64);
                        for(double step=start;step>=.125;step/=2)for(int pass=0;pass<3;pass++)
                        {
                            bool improved=false;for(int p=0;p<4;p++)foreach(int direction in new[]{-1,1})
                            {var trial=(double[])fitted.Clone();trial[p]+=step*direction;if(trial[2]<=1||trial[3]<=1)continue;double e=Error(source,Alignment(bounds,trial,match.Rotation),train).Rmse;if(e+1e-8<best){fitted=trial;best=e;improved=true;}}
                            if(!improved)break;
                        }
                        var alignment=Alignment(bounds,fitted,match.Rotation);var inverse=Inverse(alignment);
                        var exactSource=PngRows.Sample(png,validation.Select(t=>Point(inverse,t.X,t.Y)).ToArray());
                        double square=0,alphaError=0;int visible=0;
                        for(int i=0;i<validation.Count;i++){var a=exactSource[i];var b=validation[i].Value;if(Math.Max(a[3],b[3])<.03)continue;visible++;for(int k=0;k<4;k++)square+=Math.Pow(a[k]-b[k],2);alphaError+=Math.Abs(a[3]-b[3]);}
                        var error=(Rmse:visible==0?1:Math.Sqrt(square/(visible*4)),Alpha:visible==0?1:alphaError/visible,Visible:visible);
                        if(error.Visible<64||error.Rmse>.035||error.Alpha>.035){results.Add(new(id,layer.Index,"Not relinked: held-out pixel alignment check failed.",error.Rmse,error.Alpha));continue;}
                        var meshSource=sources[id];var extensionRef=meshSource.Descendants("CTextureInputExtension").Single(e=>e.Attribute("xs.ref")!=null);var extension=objects[(string)extensionRef.Attribute("xs.ref")!];
                        var inputs=Field(extension,"_textureInputs");XElement Resolve(XElement e)=>e.Attribute("xs.ref")!=null?objects[(string)e.Attribute("xs.ref")!]:e;
                        var modelInput=Resolve(inputs.Elements("CTextureInput_ModelImage").Single());var atlasInput=Resolve(inputs.Elements("CTextureInput_TextureAtlasRegion").Single());
                        var atlasToCanvas=Multiply(Matrix(Field(atlasInput,"optionalTransformOnCanvas")),Matrix(Field(atlasInput,"inputImageLocalToCanvasTransform")));
                        var positions=meshSource.Elements("float-array").Single(e=>Name(e)=="positions").Value.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(D).ToArray();var uvs=mesh.GetProperty("uvs").EnumerateArray().ToArray();double geometryError=0;
                        for(int i=0;i<uvs.Length;i++){var p=Point(atlasToCanvas,uvs[i][0].GetDouble()*width,uvs[i][1].GetDouble()*height);geometryError=Math.Max(geometryError,Math.Sqrt(Math.Pow(p.X-positions[2*i],2)+Math.Pow(p.Y-positions[2*i+1],2)));}
                        if(!double.IsFinite(geometryError)||geometryError>1){results.Add(new(id,layer.Index,"Not relinked: atlas-to-canvas fit exceeds one canvas pixel.",error.Rmse,error.Alpha,geometryError));continue;}
                        var image=modelImages[layer.Index];var optional=Multiply(Multiply(atlasToCanvas,alignment),Inverse(Matrix(Field(image,"_materialLocalToCanvasTransform"))));
                        if(!optional.All(double.IsFinite))throw new InvalidDataException("Non-finite source transform.");
                        // Restore the source-image association, as in an atlas-backed original CMO3.
                        // Keep currentTextureInputData, textureState, GPU texture and UVs on the atlas.
                        var transform=Field(modelInput,"optionalTransformOnCanvas");var guidField=Field(modelInput,"_modelImageGuid");string sourceGuid=Field(image,"guid").Attribute("xs.ref")!.Value;
                        for(int i=0;i<Fields.Length;i++)transform.SetAttributeValue(Fields[i],optional[i].ToString("R",CultureInfo.InvariantCulture));
                        guidField.SetAttributeValue("xs.ref",sourceGuid);
                        results.Add(new(id,layer.Index,"Relinked PSD source image; active atlas retained.",error.Rmse,error.Alpha,geometryError,alignment,optional));
                    }
                    catch(Exception e) when(e is InvalidDataException or InvalidOperationException or ArgumentException or IOException or KeyNotFoundException or IndexOutOfRangeException or FormatException)
                    {results.Add(new(id,match.LayerIndex,"Not relinked: "+e.Message));}
                }
            }
            catch(Exception e) when(e is IOException or ArgumentException or KeyNotFoundException or IndexOutOfRangeException)
            {foreach(var (_,m) in candidates)if(!results.Any(r=>r.MeshId==m!.MeshId))results.Add(new(m!.MeshId,m.LayerIndex,"Not relinked: "+e.Message));}
        }
        return results;
    }
}
