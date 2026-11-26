using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
namespace MocRecovery;
// Bounded row decoder for matching only; never rewrites source artwork.
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
        var (width,height)=Size(png);int type=png[25],channels=type switch{2=>3,6=>4,_=>0};
        if(png[24]!=8||png[28]!=0||channels==0)throw new InvalidDataException("Artwork matching supports non-interlaced 8-bit RGB/RGBA PNGs. Original artwork is still preserved.");
        var spans=new List<(int,int)>();
        for(int p=8;p+12<=png.Length;)
        {int n=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(p));if(n<0||n>png.Length-p-12)throw new InvalidDataException("Invalid PNG chunk length.");string tag=Encoding.ASCII.GetString(png,p+4,4);if(tag=="IDAT")spans.Add((p+8,n));if(tag=="tRNS")throw new InvalidDataException("RGB transparency-key PNG is not supported for matching.");p+=n+12;if(tag=="IEND")break;}
        using var parts=new Parts(png,spans);using var z=new ZLibStream(parts,CompressionMode.Decompress);
        byte[] previous=new byte[checked(width*channels)],row=new byte[previous.Length],rgba=new byte[checked(width*4)];
        for(int y=0;y<height;y++)
        {
            int filter=z.ReadByte();if(filter<0||filter>4)throw new InvalidDataException("Invalid PNG row filter.");z.ReadExactly(row);
            for(int i=0;i<row.Length;i++)
            {
                int a=i>=channels?row[i-channels]:0,b=previous[i],c=i>=channels?previous[i-channels]:0;
                int predictor=filter switch{0=>0,1=>a,2=>b,3=>(a+b)/2,_=>Paeth(a,b,c)};row[i]=unchecked((byte)(row[i]+predictor));
            }
            for(int x=0;x<width;x++){int s=x*channels,d=x*4;rgba[d]=row[s];rgba[d+1]=row[s+1];rgba[d+2]=row[s+2];rgba[d+3]=channels==4?row[s+3]:(byte)255;}
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
