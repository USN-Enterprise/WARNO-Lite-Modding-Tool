using System.Buffers.Binary;
using System.Text;
namespace WarnoLiteModdingTool.Core.Images;

public sealed record ArchiveImageEntry(string Archive,string Name,long Offset,int Length)
{
    public byte[] Read(){using var f=File.OpenRead(Archive);if(Offset<0||Length<0||Offset+Length>f.Length)throw new InvalidDataException("图片载荷越界");f.Position=Offset;var bytes=new byte[Length];f.ReadExactly(bytes);return bytes;}
}
public static class ImageArchive
{
    public static IEnumerable<ArchiveImageEntry> ReadDirectory(string path,Func<string,bool>? include=null)
    {
        using var f=File.OpenRead(path);using var r=new BinaryReader(f);var h=r.ReadBytes(80);
        var v=BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(4));if(!h.AsSpan(0,4).SequenceEqual("edat"u8)||v is not (2 or 3))throw new InvalidDataException("未知EDat图片包");
        var at=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(v==2?25:8));var size=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(v==2?29:12));var origin=BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(v==2?33:16));
        if(size==0)yield break;if(size>20_000_000||at+ (long)size>f.Length)throw new InvalidDataException("图片目录越界");f.Position=at;var bytes=r.ReadBytes((int)size);
        var entries=new List<ArchiveImageEntry>();Walk(9,bytes.Length,"",0);foreach(var e in entries)yield return e;
        void Walk(int start,int end,string prefix,int depth)
        {
            if(depth>64)throw new InvalidDataException("图片目录过深");
            while(start<end){if(start+8>end)throw new InvalidDataException("目录截断");var first=BitConverter.ToInt32(bytes,start);var length=BitConverter.ToInt32(bytes,start+4);var stop=length==0?end:checked(start+length);if(stop<=start||stop>end)throw new InvalidDataException("目录范围无效");var ns=start+(first!=0?8:40);if(ns>=stop)throw new InvalidDataException("名称截断");var zero=Array.IndexOf(bytes,(byte)0,ns,stop-ns);if(zero<0)throw new InvalidDataException("名称截断");var name=prefix+Encoding.UTF8.GetString(bytes,ns,zero-ns);
                if(first!=0){if(start+first<=zero)throw new InvalidDataException("目录循环");Walk(start+first,stop,name,depth+1);}else if((name.EndsWith(".atlas",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".tgv",StringComparison.OrdinalIgnoreCase))&&(include is null||include(name.Replace((char)92,'/')))){var offset=checked((long)origin+BitConverter.ToInt64(bytes,start+8));var count=BitConverter.ToInt64(bytes,start+16);if(count<0||count>300_000_000||offset<0||offset+count>f.Length)throw new InvalidDataException("图片范围无效");entries.Add(new(path,name.Replace('\\','/'),offset,(int)count));}start=stop;}
        }
    }
}
public sealed record AtlasRegion(string Source,string Texture,int X,int Y,int Width,int Height);
public sealed record ImagePixels(int Width,int Height,byte[] Rgba);
public static class GameImageDecoder
{
    public static IReadOnlyList<AtlasRegion> ReadAtlas(byte[] packed)
    {
        if(packed.Length<44||!packed.AsSpan(0,4).SequenceEqual("EUG0"u8)||!packed.AsSpan(8,4).SequenceEqual("CNDF"u8))throw new InvalidDataException("未知Atlas格式");
        var rawSize=BitConverter.ToInt32(packed,40);if(rawSize<=0||rawSize>32_000_000)throw new InvalidDataException("Atlas过大");using var dec=new ZstdSharp.Decompressor();var data=dec.Unwrap(packed.AsSpan(44),rawSize).ToArray();if(data.Length!=rawSize)throw new InvalidDataException("Atlas长度不符");
        var toc=checked((int)BitConverter.ToInt64(packed,16)-40);using var ms=new MemoryStream(data);using var r=new BinaryReader(ms);ms.Position=toc;if(r.ReadUInt32()!=0x30434f54)throw new InvalidDataException("Atlas目录缺失");var sections=new Dictionary<string,(int Start,int Length)>();var count=r.ReadInt32();if(count<0||count>32)throw new InvalidDataException("Atlas目录无效");
        for(var i=0;i<count;i++){var key=Encoding.ASCII.GetString(r.ReadBytes(8)).TrimEnd('\0');var at=checked((int)r.ReadInt64()-40);var len=checked((int)r.ReadInt64());if(at<0||len<0||at+(long)len>data.Length)throw new InvalidDataException("Atlas节越界");sections.Add(key,(at,len));}
        string ReadString(){var n=r.ReadInt32();if(n<0||n>1_000_000||ms.Position+n>ms.Length)throw new InvalidDataException("Atlas字符串无效");return Encoding.UTF8.GetString(r.ReadBytes(n)).TrimEnd('\0');}
        var strings=new List<string>();var sec=sections["STRG"];ms.Position=sec.Start;while(ms.Position<sec.Start+sec.Length)strings.Add(ReadString());
        var classes=new List<string>();sec=sections["CLAS"];ms.Position=sec.Start;while(ms.Position<sec.Start+sec.Length)classes.Add(ReadString());
        var props=new List<string>();sec=sections["PROP"];ms.Position=sec.Start;while(ms.Position<sec.Start+sec.Length){props.Add(ReadString());r.ReadInt32();}
        var objects=new List<(string Type,Dictionary<string,object> Fields)>();sec=sections["OBJE"];ms.Position=sec.Start;
        while(ms.Position<sec.Start+sec.Length){var type=classes[r.ReadInt32()];var fields=new Dictionary<string,object>();while(true){var p=r.ReadUInt32();if(p==0xabababab)break;var key=props[checked((int)p)];var tag=r.ReadInt32();object value=tag switch {28=>strings[r.ReadInt32()],31=>( (float)r.ReadInt32(),(float)r.ReadInt32()),33=>(r.ReadSingle(),r.ReadSingle()),9=>ReadRef(),_=>throw new InvalidDataException("未知Atlas属性类型")};fields.Add(key,value);}objects.Add((type,fields));}
        int ReadRef(){if(r.ReadUInt32()!=0xbbbbbbbb)throw new InvalidDataException("未知Atlas引用");var id=r.ReadInt32();if(r.ReadInt32()!=0)throw new InvalidDataException("外部Atlas引用");return id;}
        var result=new List<AtlasRegion>();foreach(var item in objects.Where(o=>o.Type=="TTextureSmall")){var v=item.Fields;var container=objects[(int)v["Container"]];var size=((float,float))container.Fields["Size"];var min=v.TryGetValue("MinUV",out var m)?((float,float))m:(0f,0f);var max=((float,float))v["MaxUV"];var pixels=((float,float))v["SizeInPixels"];var x=(int)Math.Round(min.Item1*size.Item1);var y=(int)Math.Round(min.Item2*size.Item2);var w=(int)pixels.Item1;var h=(int)pixels.Item2;if(w<=0||h<=0||x<0||y<0||x+w>size.Item1||y+h>size.Item2||Math.Abs((max.Item1-min.Item1)*size.Item1-w)>1||Math.Abs((max.Item2-min.Item2)*size.Item2-h)>1)throw new InvalidDataException("Atlas裁切范围无效");result.Add(new((string)v["TexturePartFileName"],(string)container.Fields["TextureFileName"],x,y,w,h));}return result;
    }
    public static ImagePixels ReadTgv(byte[] data)
    {
        if(data.Length<64||BitConverter.ToInt32(data,0)!=3)throw new InvalidDataException("未知TGV版本");var w=BitConverter.ToInt32(data,8);var h=BitConverter.ToInt32(data,12);var n=BitConverter.ToUInt16(data,24);if(w<=0||h<=0||w>8192||h>8192||n==0||n>20)throw new InvalidDataException("TGV尺寸无效");var format=Encoding.ASCII.GetString(data,28,16).TrimEnd('\0');if(!format.StartsWith("A8B8G8R8",StringComparison.Ordinal))throw new InvalidDataException("暂不支持此TGV颜色格式："+format);
        foreach(var table in new[]{48,44,52,56,60,64}){if(table+n*8>data.Length)continue;for(var i=0;i<n;i++){var at=BitConverter.ToInt32(data,table+i*4);var len=BitConverter.ToInt32(data,table+n*4+i*4);if(at<0||len<12||at+(long)len>data.Length||!data.AsSpan(at,4).SequenceEqual("ZSTD"u8)||BitConverter.ToInt32(data,at+4)!=checked(w*h*4))continue;using var dec=new ZstdSharp.Decompressor();var raw=dec.Unwrap(data.AsSpan(at+8,len-8),w*h*4).ToArray();if(raw.Length!=w*h*4)throw new InvalidDataException("TGV像素长度不符");return new(w,h,raw);}}throw new InvalidDataException("找不到TGV完整像素层");
    }
    public static ImagePixels Crop(ImagePixels image,AtlasRegion region)
    {
        if(region.X<0||region.Y<0||region.Width<=0||region.Height<=0||region.X+(long)region.Width>image.Width||region.Y+(long)region.Height>image.Height)throw new InvalidDataException("图片裁切越界");var bytes=new byte[checked(region.Width*region.Height*4)];for(var y=0;y<region.Height;y++)Buffer.BlockCopy(image.Rgba,((region.Y+y)*image.Width+region.X)*4,bytes,y*region.Width*4,region.Width*4);return new(region.Width,region.Height,bytes);
    }
}
