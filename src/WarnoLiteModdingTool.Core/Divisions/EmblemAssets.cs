using System.Text;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
namespace WarnoLiteModdingTool.Core.Divisions;
public sealed record EmblemAsset(string Key,string PngBase64,string DeclarationPath,string Recipe="");
public static class EmblemAssets
{
    public static string FindDeclaration(string root,string currentKey)
    {
        var directory=Path.Combine(root,"GameData","Generated","UserInterface","Textures");
        var paths=Directory.Exists(directory)?Directory.EnumerateFiles(directory,"*.ndf",SearchOption.AllDirectories):[];
        var matches=paths.Where(p=>new NdfTopLevelScanner().Scan(File.ReadAllText(p),p,"textures",root).Objects.Any(o=>o.Name==currentKey&&o.TypeName=="TUIResourceTexture_Common")).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("无法唯一定位当前师徽纹理声明文件");
        return Path.GetRelativePath(root,matches[0]).Replace('\\','/');
    }
    public static byte[] Decode(EmblemAsset asset)
    {
        if(!Regex.IsMatch(asset.Key,@"^Texture_Division_Emblem_mod_[A-Za-z0-9_]+$")||asset.PngBase64.Length>8_000_000)throw new InvalidDataException("师徽资产标识或大小无效");
        return Images.PngAssets.Decode(asset.PngBase64);
    }

    public static void Plan(string root,DivisionIdentityState state,List<PlannedFileChange> files)
    {
        var a=state.Asset!;var png=Decode(a);if(a.Key!=state.Emblem)throw new TransactionValidationException("师徽资产与引用不一致");
        var decl=TextFileSnapshot.Load(root,a.DeclarationPath,FormalTextFileKind.Ndf);
        if(!a.DeclarationPath.Replace('\\','/').StartsWith("GameData/Generated/UserInterface/Textures/",StringComparison.Ordinal))throw new TransactionValidationException("师徽声明目录无效");
        var prior=files.SingleOrDefault(f=>f.RelativePath.Equals(a.DeclarationPath,StringComparison.OrdinalIgnoreCase));var text=prior is null?decl.Text:Encoding.UTF8.GetString(prior.CandidateBytes);
        foreach(var path in Directory.EnumerateFiles(Path.Combine(root,"GameData"),"*.ndf",SearchOption.AllDirectories))
            if(Regex.IsMatch(File.ReadAllText(path),@"\b"+Regex.Escape(a.Key)+@"\s+is\b"))throw new TransactionValidationException("师徽资源标识已存在，请重新导入");
        if(Regex.IsMatch(text,@"\b"+Regex.Escape(a.Key)+@"\s+is\b"))throw new TransactionValidationException("同批师徽标识重复");
        var relative="GameData/Assets/2D/Interface/UseOutGame/Division/Emblem/"+a.Key+".png";
        var snap=TextFileSnapshot.Load(root,relative,FormalTextFileKind.Binary,true);
        if(snap.Existed||files.Any(f=>f.RelativePath.Equals(relative,StringComparison.OrdinalIgnoreCase)))throw new TransactionValidationException("师徽图片已存在，不覆盖");
        text+=decl.NewLine+$"{a.Key} is TUIResourceTexture_Common"+decl.NewLine+"("+decl.NewLine+$"    FileName = 'GameData:/{relative[9..]}'"+decl.NewLine+")"+decl.NewLine;
        if(prior is not null)files.Remove(prior);files.Add(UnitApplyPlanner.ToWriteChange(decl,text,["新增独立师徽纹理"]));
        files.Add(new(relative,snap.FullPath,FormalTextFileKind.Binary,PlannedFileAction.Write,false,[],png,null,["新增师徽PNG"]));
    }
}
