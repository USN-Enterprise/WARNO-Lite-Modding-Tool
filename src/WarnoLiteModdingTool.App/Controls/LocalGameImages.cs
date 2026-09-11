using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarnoLiteModdingTool.Core.Images;
using WarnoLiteModdingTool.App.Settings;
namespace WarnoLiteModdingTool.App.Controls;
public static class LocalGameImages
{
    private static readonly SemaphoreSlim Gate=new(1);
    private static string _stamp="";
    private static Dictionary<string,ArchiveImageEntry> _entries=new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string,IReadOnlyList<AtlasRegion>> Atlases=new(StringComparer.OrdinalIgnoreCase);
    private static (string Name,ImagePixels Pixels)? _last;
    public static async Task<BitmapSource?> LoadAsync(string root,string source,CancellationToken token)
    {
        await Gate.WaitAsync(token);try{return await Task.Run(()=>Load(root,source,token),token);}finally{Gate.Release();}
    }
    private static BitmapSource? Load(string root,string source,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var local=ModTextures.LocalPath(root,source);if(local is not null&&File.Exists(local))return ReadBitmap(local);
        var game=new UiSettings().Load().GameDirectory;if(string.IsNullOrWhiteSpace(game))game=ModFinder.FindRoots(null,token).Select(Path.GetDirectoryName).FirstOrDefault(p=>p is not null&&Directory.Exists(Path.Combine(p,"Data","PC")));if(game is null||!Directory.Exists(Path.Combine(game,"Data","PC")))return null;
        var packages=Directory.EnumerateFiles(Path.Combine(game,"Data","PC"),"ZZ_*.dat",SearchOption.AllDirectories).OrderBy(p=>string.Join("/",p.Split(Path.DirectorySeparatorChar).Where(s=>ulong.TryParse(s,out _)).Select(s=>ulong.Parse(s).ToString("D20"))),StringComparer.Ordinal).ThenBy(p=>p,StringComparer.Ordinal).ToArray();
        var stamp=string.Join("|",packages.Select(p=>{var f=new FileInfo(p);return p+":"+f.Length+":"+f.LastWriteTimeUtc.Ticks;}));
        var cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WarnoLiteModdingTool","images","v1",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)))[..20]);var cached=Path.Combine(cache,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))+".png");
        if(File.Exists(cached)){try{return ReadBitmap(cached);}catch(Exception e)when(e is IOException or NotSupportedException or FileFormatException){File.Delete(cached);}}
        if(_stamp!=stamp){_entries=new(StringComparer.OrdinalIgnoreCase);Atlases.Clear();_last=null;foreach(var package in packages){token.ThrowIfCancellationRequested();foreach(var entry in ImageArchive.ReadDirectory(package,n=>n.Contains("/Assets/2D/",StringComparison.OrdinalIgnoreCase)||n.StartsWith("PC/Atlas/",StringComparison.OrdinalIgnoreCase)&&!n.Contains("/Assets/3D/",StringComparison.OrdinalIgnoreCase)))_entries[entry.Name]=entry;}_stamp=stamp;}
        var direct=source.Replace("GameData:/","PC/Texture/",StringComparison.OrdinalIgnoreCase);direct=Path.ChangeExtension(direct,".tgv").Replace('\\','/');ImagePixels? pixels=null;
        if(_entries.TryGetValue(direct,out var single))pixels=GameImageDecoder.ReadTgv(single.Read());
        else foreach(var entry in _entries.Values.Where(e=>e.Name.EndsWith(".atlas",StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested();if(!Atlases.TryGetValue(entry.Name,out var regions)){try{regions=GameImageDecoder.ReadAtlas(entry.Read());}catch(Exception e)when(e is InvalidDataException or IOException or ZstdSharp.ZstdException or IndexOutOfRangeException or KeyNotFoundException or ArgumentException or OverflowException){regions=[];}Atlases[entry.Name]=regions;}
            var region=regions.FirstOrDefault(r=>r.Source.Equals(source,StringComparison.OrdinalIgnoreCase));if(region is null)continue;var name=Path.ChangeExtension(region.Texture.Replace("ZZ:/","",StringComparison.OrdinalIgnoreCase),".tgv").Replace('\\','/');if(!_entries.TryGetValue(name,out var texture))return null;if(_last?.Name!=name)_last=(name,GameImageDecoder.ReadTgv(texture.Read()));pixels=GameImageDecoder.Crop(_last.Value.Pixels,region);break;
        }
        if(pixels is null)return null;token.ThrowIfCancellationRequested();var bgra=pixels.Rgba.ToArray();for(var i=0;i<bgra.Length;i+=4)(bgra[i],bgra[i+2])=(bgra[i+2],bgra[i]);var bitmap=BitmapSource.Create(pixels.Width,pixels.Height,96,96,PixelFormats.Bgra32,null,bgra,pixels.Width*4);bitmap.Freeze();Directory.CreateDirectory(cache);var temp=cached+".tmp";try{using(var stream=File.Create(temp)){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(stream);}File.Move(temp,cached,true);}finally{if(File.Exists(temp))File.Delete(temp);}return bitmap;
    }
    private static BitmapSource ReadBitmap(string path){using var stream=File.OpenRead(path);var frame=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);frame.Freeze();return frame;}
}
