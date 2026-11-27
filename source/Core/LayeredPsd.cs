using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace MocRecovery;

// Builds a paintable default-pose document from the generated project and runtime atlas.
internal static class LayeredPsd
{
    private sealed record Layer(string Name,int Left,int Top,int Width,int Height,byte Opacity,int Divider,int[] Lengths);
    private readonly record struct Point(double X,double Y);
    private static XElement Field(XElement x,string name)=>x.Descendants().First(e=>(string?)e.Attribute("xs.n")==name);
    private static string Reference(XElement e)=>(string)e.Attribute("xs.ref")!;
    private static string Label(XElement e)=>Field(e,"localName").Value;
    private static Point[] Points(XElement mesh,string field)
    {
        var values=mesh.Elements("float-array").Single(e=>(string?)e.Attribute("xs.n")==field).Value
            .Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
        return Enumerable.Range(0,values.Length/2).Select(i=>new Point(values[i*2],values[i*2+1])).ToArray();
    }

    internal static List<string> Build(string path,XDocument doc,JsonElement model,IReadOnlyList<string> pages,Action<string> progress,CancellationToken token)
    {
        var canvas=doc.Descendants("CImageCanvas").First();
        int width=(int)Field(canvas,"pixelWidth"),height=(int)Field(canvas,"pixelHeight");
        if(width is <1 or >30000 || height is <1 or >30000 || (long)width*height>134_217_728)
            throw new InvalidDataException("Working PSD exceeds the supported canvas size (30,000 pixels per side and 128 Mi pixels).");
        var preview=new byte[checked(width*height*4)];
        var meshes=doc.Descendants("CArtMeshSource").Where(e=>e.HasElements).ToDictionary(e=>Reference(e.Descendants("CDrawableGuid").Single(g=>(string?)g.Attribute("xs.n")=="guid")));
        var parts=doc.Descendants("CPartSource").Where(e=>e.HasElements).ToDictionary(e=>Reference(Field(e,"guid")));
        var runtime=model.GetProperty("art_meshes").EnumerateArray().ToDictionary(m=>m.GetProperty("id").GetString()!);
        var parameters=model.GetProperty("parameters");
        var bands=model.GetProperty("binding_bands");
        var notes=new List<string>();
        var layers=new List<Layer>();
        var visited=new HashSet<string>();
        using var channels=new FileStream(path+".channels",FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,65536,FileOptions.DeleteOnClose);
        int cachedPage=-1,atlasWidth=0,atlasHeight=0,rasterCount=0;
        byte[] atlas=[];
        double DefaultValue(JsonElement mesh,string property)
        {
            var band=mesh.GetProperty("binding_band_index");
            JsonElement[] axes=band.ValueKind==JsonValueKind.Null?[]:bands[band.GetInt32()].GetProperty("bindings").EnumerateArray().ToArray();
            double[] defaults=axes.Select(a=>parameters[a.GetProperty("parameter_index").GetInt32()].GetProperty("default").GetDouble()).ToArray();
            var forms=mesh.GetProperty("keyforms");
            return RecoveryValidator.Weights(axes,defaults).Sum(k=>k.Weight*forms[k.Index].GetProperty(property).GetDouble());
        }
        void Mesh(XElement source)
        {
            string id=(string)Field(source,"id").Attribute("idstr")!;
            var mesh=runtime[id];
            int page=mesh.GetProperty("texture_index").GetInt32();
            if((uint)page>=pages.Count || !File.Exists(pages[page])){notes.Add("Working PSD skipped "+id+": missing atlas page "+page);return;}
            if(page!=cachedPage)
            {
                var png=File.ReadAllBytes(pages[page]);
                (atlasWidth,atlasHeight)=PngRows.Size(png);
                atlas=new byte[checked(atlasWidth*atlasHeight*4)];
                PngRows.Read(png,(y,row)=>{token.ThrowIfCancellationRequested();row.CopyTo(atlas,y*atlasWidth*4);});
                cachedPage=page;
            }
            progress("Building working PSD: "+id);
            var positions=Points(source,"positions");var uv=Points(source,"uvs");
            int[] indices=mesh.GetProperty("triangle_indices").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
            if(indices.Length==0){notes.Add("Working PSD skipped "+id+": no triangles.");return;}
            int left=Math.Clamp((int)Math.Floor(positions.Min(p=>p.X)),0,width),top=Math.Clamp((int)Math.Floor(positions.Min(p=>p.Y)),0,height);
            int right=Math.Clamp((int)Math.Ceiling(positions.Max(p=>p.X)),0,width),bottom=Math.Clamp((int)Math.Ceiling(positions.Max(p=>p.Y)),0,height);
            int w=right-left,h=bottom-top;
            if(w<=0 || h<=0){notes.Add("Working PSD skipped "+id+": outside canvas or empty bounds.");return;}
            var rgba=new byte[checked(w*h*4)];
            for(int i=0;i<indices.Length;i+=3)
            {
                int ia=indices[i],ib=indices[i+1],ic=indices[i+2];
                var a=positions[ia];var b=positions[ib];var c=positions[ic];
                double dx=b.X-a.X,dy=b.Y-a.Y,ex=c.X-a.X,ey=c.Y-a.Y,det=dx*ey-dy*ex;
                if(Math.Abs(det)<1e-10)continue;
                int x0=Math.Max(left,(int)Math.Floor(Math.Min(a.X,Math.Min(b.X,c.X))));
                int x1=Math.Min(right,(int)Math.Ceiling(Math.Max(a.X,Math.Max(b.X,c.X))));
                int y0=Math.Max(top,(int)Math.Floor(Math.Min(a.Y,Math.Min(b.Y,c.Y))));
                int y1=Math.Min(bottom,(int)Math.Ceiling(Math.Max(a.Y,Math.Max(b.Y,c.Y))));
                for(int y=y0;y<y1;y++)
                {
                    token.ThrowIfCancellationRequested();
                    for(int x=x0;x<x1;x++)
                    {
                        double px=x+.5-a.X,py=y+.5-a.Y;
                        double v=(px*ey-py*ex)/det,u=(dx*py-dy*px)/det,t=1-v-u;
                        if(Math.Min(t,Math.Min(u,v)) < -1e-9)continue;
                        int sx=Math.Clamp((int)Math.Floor((t*uv[ia].X+v*uv[ib].X+u*uv[ic].X)*atlasWidth),0,atlasWidth-1);
                        int sy=Math.Clamp((int)Math.Floor((t*uv[ia].Y+v*uv[ib].Y+u*uv[ic].Y)*atlasHeight),0,atlasHeight-1);
                        atlas.AsSpan(4*(sy*atlasWidth+sx),4).CopyTo(rgba.AsSpan(4*((y-top)*w+x-left),4));
                    }
                }
            }
            double opacity=DefaultValue(mesh,"opacity");
            byte alpha=(byte)Math.Clamp(Math.Round(opacity*255),0,255);
            int[] lengths=new int[4];
            for(int ch=0;ch<4;ch++)
            {
                long start=channels.Position;U16(channels,2);
                using(var deflate=new ZLibStream(channels,CompressionLevel.Fastest,true))
                    Plane(deflate,rgba,w,h,ch,token);
                lengths[ch]=checked((int)(channels.Position-start));
            }
            layers.Add(new(Label(source),left,top,w,h,alpha,0,lengths));rasterCount++;
            for(int y=0;y<h;y++)
            {
                token.ThrowIfCancellationRequested();
                for(int x=0;x<w;x++)
                {
                    int i=4*(y*w+x),j=4*((top+y)*width+left+x);
                    int sa=(rgba[i+3]*alpha+127)/255;
                    if(sa==0)continue;
                    int back=(preview[j+3]*(255-sa)+127)/255,oa=sa+back;
                    for(int ch=0;ch<3;ch++)preview[j+ch]=(byte)((rgba[i+ch]*sa+preview[j+ch]*back+oa/2)/oa);
                    preview[j+3]=(byte)oa;
                }
            }
        }
        void Part(XElement part,bool root)
        {
            if(!visited.Add(Reference(Field(part,"guid"))))throw new InvalidDataException("Repeated Part in PSD layer hierarchy.");
            if(!root)layers.Add(new("</Layer group>",0,0,0,0,255,3,[]));
            double DrawOrder(XElement child)
            {
                if(child.Name!="CDrawableGuid")return 500;
                var source=meshes[Reference(child)];
                return DefaultValue(runtime[(string)Field(source,"id").Attribute("idstr")!],"draw_order");
            }
            foreach(var child in Field(part,"_childGuids").Elements().Reverse().OrderBy(DrawOrder))
            {
                token.ThrowIfCancellationRequested();
                if(child.Name=="CPartGuid")Part(parts[Reference(child)],false);
                else if(child.Name=="CDrawableGuid")Mesh(meshes[Reference(child)]);
            }
            if(!root)layers.Add(new(Label(part),0,0,0,0,255,1,[]));
        }
        Part(parts.Values.Single(e=>(string?)e.Element("CPartId")?.Attribute("idstr")=="__RootPart__"),true);
        if(rasterCount==0)throw new InvalidDataException("No ArtMesh with usable triangles and an available atlas page.");
        using var output=File.Create(path);
        Tag(output,"8BPS");U16(output,1);output.Write(new byte[6]);U16(output,4);U32(output,height);U32(output,width);
        U16(output,8);U16(output,3);U32(output,0);U32(output,0);
        long section=output.Position;U32(output,0);
        long info=output.Position;U32(output,0);U16(output,-checked((short)layers.Count));
        foreach(var layer in layers)Record(output,layer);
        channels.Position=0;
        byte[] buffer=new byte[65536];int n;
        while((n=channels.Read(buffer))>0){token.ThrowIfCancellationRequested();output.Write(buffer,0,n);}
        if((output.Position&1)!=0)output.WriteByte(0);
        Finish(output,info);U32(output,0);Finish(output,section);
        WriteComposite(output,preview,width,height,token);
        notes.Insert(0,$"Working PSD: {rasterCount} ArtMesh layers, {width} × {height}, grouped by recovered Part.");
        return notes;
    }

    // Photoshop uses a 16-bit compressed byte count per row for PSD PackBits.
    private static void WriteComposite(Stream output,byte[] rgba,int width,int height,CancellationToken token)
    {
        U16(output,1);
        long table=output.Position;
        byte[] lengths=new byte[height*4*2];
        output.Write(lengths);
        var row=new byte[width];
        var packed=new byte[width*2];
        for(int channel=0;channel<4;channel++)
        for(int y=0;y<height;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=0,i=y*width*4;x<width;x++,i+=4)
            {
                // The merged transparency channel uses a white matte; layer pixels are straight RGBA.
                row[x]=channel==3 ? rgba[i+3] : (byte)(255-((255-rgba[i+channel])*rgba[i+3]+127)/255);
            }
            int pos=0,n=0;
            while(pos<width)
            {
                int begin=pos++;
                while(pos<width && pos-begin<128 && row[pos]==row[begin])pos++;
                if(pos-begin>=3)
                {
                    packed[n++]=(byte)(257-(pos-begin));packed[n++]=row[begin];
                }
                else
                {
                    while(pos<width && pos-begin<128 &&
                        !(pos+2<width && row[pos]==row[pos+1] && row[pos]==row[pos+2]))pos++;
                    packed[n++]=(byte)(pos-begin-1);
                    row.AsSpan(begin,pos-begin).CopyTo(packed.AsSpan(n));n+=pos-begin;
                }
            }
            BinaryPrimitives.WriteUInt16BigEndian(lengths.AsSpan(2*(channel*height+y)),checked((ushort)n));
            output.Write(packed,0,n);
            if(output.Position>int.MaxValue)throw new InvalidDataException("Working PSD exceeds the 2 GB PSD file limit.");
        }
        long end=output.Position;output.Position=table;output.Write(lengths);output.Position=end;
    }

    private static void Plane(Stream target,byte[] rgba,int width,int height,int channel,CancellationToken token)
    {
        var row=new byte[width];
        for(int y=0;y<height;y++)
        {
            token.ThrowIfCancellationRequested();
            for(int x=0,i=4*y*width+channel;x<width;x++,i+=4)row[x]=rgba[i];
            target.Write(row);
        }
    }
    private static void Record(Stream s,Layer l)
    {
        U32(s,l.Top);U32(s,l.Left);U32(s,l.Top+l.Height);U32(s,l.Left+l.Width);U16(s,l.Lengths.Length);
        for(int i=0;i<l.Lengths.Length;i++){U16(s,i==3?-1:i);U32(s,l.Lengths[i]);}
        Tag(s,"8BIM");Tag(s,"norm");s.WriteByte(l.Opacity);s.WriteByte(0);s.WriteByte(0);s.WriteByte(0);
        long extra=s.Position;U32(s,0);U32(s,0);U32(s,0);
        byte[] name=Encoding.ASCII.GetBytes(l.Name);int len=Math.Min(255,name.Length);
        s.WriteByte((byte)len);s.Write(name,0,len);s.Write(new byte[(4-(1+len)%4)%4]);
        byte[] unicode=Encoding.BigEndianUnicode.GetBytes(l.Name);
        Tag(s,"8BIM");Tag(s,"luni");U32(s,4+unicode.Length);U32(s,l.Name.Length);s.Write(unicode);
        if(l.Divider!=0){Tag(s,"8BIM");Tag(s,"lsct");U32(s,4);U32(s,l.Divider);}
        Finish(s,extra);
    }
    private static void Finish(Stream s,long field){long end=s.Position;s.Position=field;U32(s,checked((int)(end-field-4)));s.Position=end;}
    private static void Tag(Stream s,string tag)=>s.Write(Encoding.ASCII.GetBytes(tag));
    private static void U16(Stream s,int value){Span<byte> b=stackalloc byte[2];BinaryPrimitives.WriteUInt16BigEndian(b,unchecked((ushort)value));s.Write(b);}
    private static void U32(Stream s,int value){Span<byte> b=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(b,value);s.Write(b);}
}
