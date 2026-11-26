using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
namespace MocRecovery;
// Bounded row decoder for atlas cropping and artwork matching; never rewrites source artwork.
public static class PngRows
{
    public static (int Width,int Height) Size(byte[] png)
    {
        if(png.Length<33||!png.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new InvalidDataException("Invalid PNG.");
        int w=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)),h=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        if(w<=0||h<=0||w>30000||h>30000||(long)w*h>180_000_000)throw new InvalidDataException("PNG dimensions exceed matching limits.");return(w,h);
    }
    public static void Read(byte[] png,Action<int,byte[]> consume)
    {
        var (width, height) = Size(png);
        int depth = png[24], type = png[25];
        bool indexed = type == 3;
        int channels = type switch { 2 => 3, 6 => 4, _ => 1 };
        bool supported = indexed ? depth is 1 or 2 or 4 or 8 : (type is 2 or 6) && depth == 8;
        if (!supported || png[28] != 0)
            throw new InvalidDataException("Artwork recovery supports non-interlaced indexed PNGs and 8-bit RGB/RGBA PNGs.");
        var spans = new List<(int,int)>();
        byte[] palette = [];
        bool hasTransparency = false;
        for (int p = 8; p <= png.Length - 12;)
        {
            int n = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(p));
            if (n < 0 || n > png.Length - p - 12) throw new InvalidDataException("Invalid PNG chunk length.");
            string tag = Encoding.ASCII.GetString(png, p + 4, 4);
            var data = png.AsSpan(p + 8, n);
            if (indexed && tag == "PLTE")
            {
                if (palette.Length != 0 || spans.Count != 0 || n == 0 || n % 3 != 0 || n / 3 > (1 << depth))
                    throw new InvalidDataException("Invalid indexed PNG palette.");
                palette = new byte[n / 3 * 4];
                for (int i = 0; i < n / 3; i++)
                {
                    data.Slice(i * 3, 3).CopyTo(palette.AsSpan(i * 4));
                    palette[i * 4 + 3] = 255;
                }
            }
            if (tag == "tRNS")
            {
                if (!indexed) throw new InvalidDataException("Transparency-key PNGs are unsupported for artwork recovery.");
                if (hasTransparency || spans.Count != 0 || n == 0 || n > palette.Length / 4)
                    throw new InvalidDataException("Invalid indexed PNG transparency table.");
                for (int i = 0; i < n; i++) palette[i * 4 + 3] = data[i];
                hasTransparency = true;
            }
            if (tag == "IDAT")
            {
                if (indexed && palette.Length == 0) throw new InvalidDataException("Indexed PNG has no palette.");
                spans.Add((p + 8, n));
            }
            p += n + 12;
            if (tag == "IEND") break;
        }
        int rowBytes = indexed ? (width * depth + 7) / 8 : width * channels;
        using var parts=new Parts(png,spans);using var z=new ZLibStream(parts,CompressionMode.Decompress);
        byte[] previous=new byte[rowBytes],row=new byte[previous.Length],rgba=new byte[checked(width*4)];
        for(int y=0;y<height;y++)
        {
            int filter=z.ReadByte();if(filter<0||filter>4)throw new InvalidDataException("Invalid PNG row filter.");z.ReadExactly(row);
            for(int i=0;i<row.Length;i++)
            {
                int a=i>=channels?row[i-channels]:0,b=previous[i],c=i>=channels?previous[i-channels]:0;
                int predictor=filter switch{0=>0,1=>a,2=>b,3=>(a+b)/2,_=>Paeth(a,b,c)};row[i]=unchecked((byte)(row[i]+predictor));
            }
            if (indexed)
            {
                int mask = (1 << depth) - 1;
                for (int x = 0; x < width; x++)
                {
                    int bit = x * depth;
                    int shift = 8 - depth - (bit % 8);
                    int index = (row[bit / 8] >> shift) & mask;
                    if (index >= palette.Length / 4) throw new InvalidDataException("PNG pixel references a missing palette entry.");
                    palette.AsSpan(index * 4, 4).CopyTo(rgba.AsSpan(x * 4));
                }
            }
            else for(int x=0;x<width;x++){int s=x*channels,d=x*4;rgba[d]=row[s];rgba[d+1]=row[s+1];rgba[d+2]=row[s+2];rgba[d+3]=channels==4?row[s+3]:(byte)255;}
            consume(y,rgba);(row,previous)=(previous,row);
        }
    }
    public static double[][] Sample(byte[] png,IReadOnlyList<(double X,double Y)> points)
    {
        var(width,height)=Size(png);var result=points.Select(_=>new double[4]).ToArray();var rows=new Dictionary<int,List<(int Index,int X,double Weight)>>();
        for(int i=0;i<points.Count;i++)
        {
            var p=points[i];if(!double.IsFinite(p.X)||!double.IsFinite(p.Y)||p.X<-.5||p.Y<-.5||p.X>width+.5||p.Y>height+.5)continue;
            double x=p.X-.5,y=p.Y-.5;int x0=(int)Math.Floor(x),y0=(int)Math.Floor(y);double fx=x-x0,fy=y-y0;
            for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)
            {int xx=x0+dx,yy=y0+dy;if(xx<0||xx>=width||yy<0||yy>=height)continue;double weight=(dx==0?1-fx:fx)*(dy==0?1-fy:fy);if(!rows.TryGetValue(yy,out var list))rows[yy]=list=[];list.Add((i,xx,weight));}
        }
        Read(png,(y,row)=>{if(!rows.TryGetValue(y,out var list))return;foreach(var (index,x,weight) in list){int p=x*4;double alpha=row[p+3]/255.0;for(int k=0;k<3;k++)result[index][k]+=row[p+k]/255.0*alpha*weight;result[index][3]+=alpha*weight;}});
        return result;
    }
    static int Paeth(int a,int b,int c){int p=a+b-c,pa=Math.Abs(p-a),pb=Math.Abs(p-b),pc=Math.Abs(p-c);return pa<=pb&&pa<=pc?a:pb<=pc?b:c;}
    sealed class Parts(byte[] bytes,List<(int Offset,int Length)> spans):Stream
    {
        int index,position;
        public override int Read(byte[] b,int o,int n)=>Read(b.AsSpan(o,n));
        public override int Read(Span<byte> output){int done=0;while(!output.IsEmpty&&index<spans.Count){var s=spans[index];int n=Math.Min(output.Length,s.Length-position);bytes.AsSpan(s.Offset+position,n).CopyTo(output);done+=n;position+=n;output=output[n..];if(position==s.Length){index++;position=0;}}return done;}
        public override bool CanRead=>true;public override bool CanWrite=>false;public override bool CanSeek=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
    }
}
