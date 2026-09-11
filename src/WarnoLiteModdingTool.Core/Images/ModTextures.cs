using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;
namespace WarnoLiteModdingTool.Core.Images;
public sealed record TextureChoice(string Key,string Source);
public static class ModTextures
{
    public static string? UnitKey(UnitRecord unit)
    {
        var doc=new NdfSyntaxDocument(UnitCreation.Source(unit));var fields=doc.FindConstructors("TUnitUIModuleDescriptor").SelectMany(c=>doc.FindDirectAssignments(c,"ButtonTexture")).ToArray();return fields.Length==1?NdfSyntaxDocument.Unquote(doc.Raw(fields[0])):null;
    }
    public static IReadOnlyList<TextureChoice> Read(string root,bool divisions=false)
    {
        var directory=Path.Combine(root,"GameData","Generated","UserInterface","Textures");if(!Directory.Exists(directory))return [];
        var results=new List<TextureChoice>();foreach(var path in Directory.EnumerateFiles(directory,"*.ndf",SearchOption.AllDirectories))
        {
            var text=File.ReadAllText(path);var doc=new NdfSyntaxDocument(text);
            if(divisions){var scan=new NdfTopLevelScanner().Scan(text,path,"textures",root);foreach(var o in scan.Objects.Where(o=>o.TypeName=="TUIResourceTexture_Common")){var sub=new NdfSyntaxDocument(text,o.CharacterOffset,o.CharacterLength);var fields=sub.FindAssignmentsAnywhere("FileName");if(fields.Count==1)results.Add(new(o.Name,NdfSyntaxDocument.Unquote(sub.Raw(fields[0]))));}}
            else foreach(var bank in doc.FindConstructors("TBUCKToolAdditionalTextureBank"))foreach(var map in doc.FindDirectAssignments(bank,"Textures"))foreach(var entry in doc.ReadMapEntries(map))
            {
                var states=doc.ReadMapEntries(entry.Value).Where(e=>NdfSyntaxDocument.Leaf(doc.Raw(e.Key))=="Normal").ToArray();if(states.Length!=1)continue;var constructors=doc.FindConstructors("TUIResourceTexture",states[0].Value);if(constructors.Count!=1)continue;var fields=doc.FindDirectAssignments(constructors[0],"FileName");if(fields.Count==1)results.Add(new(NdfSyntaxDocument.Unquote(doc.Raw(entry.Key)),NdfSyntaxDocument.Unquote(doc.Raw(fields[0]))));
            }
        }
        return results.GroupBy(x=>x.Key,StringComparer.Ordinal).Where(g=>g.Count()==1).Select(g=>g.Single()).ToArray();
    }
    public static string? LocalPath(string root,string source)
    {
        if(!source.StartsWith("GameData:/",StringComparison.OrdinalIgnoreCase))return null;var basePath=Path.GetFullPath(Path.Combine(root,"GameData"))+Path.DirectorySeparatorChar;var path=Path.GetFullPath(Path.Combine(basePath,source[10..].Replace('/',Path.DirectorySeparatorChar)));return path.StartsWith(basePath,StringComparison.OrdinalIgnoreCase)?path:null;
    }
}
