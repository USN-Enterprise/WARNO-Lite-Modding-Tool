using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;
namespace WarnoLiteModdingTool.Core.Images;
public sealed record TextureChoice(string Key,string Source);
public static class ModTextures
{
    private static readonly object PreviewGate=new();
    private static string? _previewStamp;
    private static IReadOnlyList<TextureChoice> _previewChoices=[];
    private static IReadOnlyList<TextureChoice> UnitChoices(string root)
    {
        // This cache is display-only. UnitPictures.Plan always reads fresh inputs.
        var paths=Units.UnitProjectGraph.InputFiles(root).Select(p=>new FileInfo(Path.Combine(root,p))).ToArray();
        var stamp=Path.GetFullPath(root)+"|"+string.Join("|",paths.Select(p=>p.FullName+":"+p.Length+":"+p.LastWriteTimeUtc.Ticks));
        lock(PreviewGate)
        {
            if(_previewStamp==stamp)return _previewChoices;
            var choices=UnitPictures.Catalog(root).GroupBy(t=>t.Key).Where(g=>g.Count()==1 && g.Single().Source.Length>0).Select(g=>new TextureChoice(g.Key,g.Single().Source)).ToArray();
            _previewChoices=choices;_previewStamp=stamp;return choices;
        }
    }
    public static string? UnitKey(UnitRecord unit)
    {
        var doc=new NdfSyntaxDocument(UnitCreation.Source(unit));var fields=doc.FindConstructors("TUnitUIModuleDescriptor").SelectMany(c=>doc.FindDirectAssignments(c,"ButtonTexture")).ToArray();return fields.Length==1?NdfSyntaxDocument.Unquote(doc.Raw(fields[0])):null;
    }
    public static IReadOnlyList<TextureChoice> Read(string root,bool divisions=false)
    {
        if(!divisions)return UnitChoices(root);
        var directory=Path.Combine(root,"GameData","Generated","UserInterface","Textures");if(!Directory.Exists(directory))return [];
        var results=new List<TextureChoice>();foreach(var path in Directory.EnumerateFiles(directory,"*.ndf",SearchOption.AllDirectories))
        {
            var text=File.ReadAllText(path);
            var scan=new NdfTopLevelScanner().Scan(text,path,"textures",root);
            foreach(var o in scan.Objects.Where(o=>o.TypeName=="TUIResourceTexture_Common"))
            {
                var sub=new NdfSyntaxDocument(text,o.CharacterOffset,o.CharacterLength);
                var fields=sub.FindAssignmentsAnywhere("FileName");
                if(fields.Count==1)results.Add(new(o.Name,NdfSyntaxDocument.Unquote(sub.Raw(fields[0]))));
            }
        }
        return results.GroupBy(x=>x.Key,StringComparer.Ordinal).Where(g=>g.Count()==1).Select(g=>g.Single()).ToArray();
    }
    public static string? LocalPath(string root,string source)
    {
        if(!source.StartsWith("GameData:/",StringComparison.OrdinalIgnoreCase))return null;
        try
        {
            var path=Transactions.TextFileSnapshot.ResolveInsideRoot(root,"GameData/"+source[10..]);
            var assets=Path.GetFullPath(Path.Combine(root,"GameData"))+Path.DirectorySeparatorChar;
            return path.StartsWith(assets,StringComparison.OrdinalIgnoreCase)?path:null;
        }
        catch(InvalidDataException){return null;}
    }
}
