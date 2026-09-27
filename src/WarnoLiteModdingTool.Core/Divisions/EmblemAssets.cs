using System.Text;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
namespace WarnoLiteModdingTool.Core.Divisions;
public sealed record EmblemAsset(string Key,string PngBase64,string DeclarationPath,string Recipe="");
public static class EmblemAssets
{
    public static bool IsCustom(string key) => Regex.IsMatch(key, @"^Texture_Division_Emblem_mod_[A-Za-z0-9_]+$");
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
        var referenceKey = "";
        if (state.Baselines.TryGetValue(state.Mother, out var mother))
        {
            var doc = new NdfSyntaxDocument(mother);
            var values = doc.FindConstructors("TDeckDivisionDescriptor").SelectMany(n => doc.FindDirectAssignments(n, "EmblemTexture")).ToArray();
            if (values.Length == 1) referenceKey = NdfSyntaxDocument.Unquote(doc.Raw(values[0]));
        }
        Register(root, a.DeclarationPath, a.Key, referenceKey, files, false);
    }

    // Re-selecting an existing tool-created emblem repairs registration without importing a new image.
    public static void Repair(string root, string key, List<PlannedFileChange> files, Dictionary<string, byte[]> imageDependencies)
    {
        if (!IsCustom(key)) return;
        var path = FindDeclaration(root, key);
        var graph = new UnitProjectGraph(root, files);
        if (graph.FindObjects(key).Count != 1) throw new TransactionValidationException("师徽纹理声明重复：" + key);
        var texture = graph.RequireObject(path, key);
        var doc = new NdfSyntaxDocument(graph.Body(texture));
        var values = doc.FindDirectAssignments(doc.FindConstructors("TUIResourceTexture_Common").Single(), "FileName");
        if (values.Count != 1) throw new TransactionValidationException("师徽图片路径无法唯一定位");
        var source = NdfSyntaxDocument.Unquote(doc.Raw(values[0]));
        var full = Images.ModTextures.LocalPath(root, source);
        if (full is null || !File.Exists(full)) throw new TransactionValidationException("自定义师徽PNG缺失，不能补登记：" + key);
        var bytes = File.ReadAllBytes(full); _ = Images.PngAssets.Decode(Convert.ToBase64String(bytes));
        imageDependencies[Path.GetRelativePath(root, full).Replace('\\', '/')] = bytes;
        Register(root, path, key, key, files, true);
    }

    private sealed record Bank(UnitProjectGraph.Source File, NdfValueSpan Map, IReadOnlyList<NdfMapEntry> Entries);

    private static IReadOnlyList<Bank> Banks(UnitProjectGraph graph, string key, string referenceKey, string declarationPath)
    {
        var result = new List<Bank>();
        foreach (var file in graph.Files.Values)
        foreach (var node in file.Syntax.FindConstructors("TBUCKToolAdditionalTextureBank"))
        {
            var maps = file.Syntax.FindDirectAssignments(node, "Textures");
            // Unknown unrelated banks do not disable emblem editing. Relevant ones cannot be guessed.
            var body = file.Text[file.Syntax.Tokens[node.TypeTokenIndex].Start..file.Syntax.Tokens[node.CloseTokenIndex].End];
            var relevant = file.Path.Equals(UnitProjectGraph.Normalize(declarationPath), StringComparison.OrdinalIgnoreCase) ||
                body.Contains('"' + key + '"', StringComparison.Ordinal) || body.Contains("'" + key + "'", StringComparison.Ordinal) ||
                referenceKey.Length > 0 && (body.Contains('"' + referenceKey + '"', StringComparison.Ordinal) || body.Contains("'" + referenceKey + "'", StringComparison.Ordinal));
            if (maps.Count != 1 || !DirectMap(file.Syntax, maps[0]))
            {
                if (relevant) throw new TransactionValidationException("师徽纹理库不是可唯一定位的直接MAP：" + file.Path);
                continue;
            }
            result.Add(new(file, maps[0], file.Syntax.ReadMapEntries(maps[0])));
        }
        return result;
    }

    private static bool DirectMap(NdfSyntaxDocument doc, NdfValueSpan span)
    {
        if (span.EndTokenIndex < span.StartTokenIndex + 2 || doc.Tokens[span.StartTokenIndex].Text != "MAP" || doc.Tokens[span.StartTokenIndex + 1].Text != "[") return false;
        var depth = 0;
        for (var i = span.StartTokenIndex + 1; i <= span.EndTokenIndex; i++)
        {
            if (doc.Tokens[i].Text == "[") depth++;
            else if (doc.Tokens[i].Text == "]" && --depth == 0)
                return i == span.EndTokenIndex && doc.ReadArrayElements(new(span.StartTokenIndex + 1, span.EndTokenIndex)).Count == doc.ReadMapEntries(span).Count;
        }
        return false;
    }

    private static string Normal(Bank bank, NdfMapEntry entry)
    {
        var doc = bank.File.Syntax;
        if (!DirectMap(doc, entry.Value)) throw new TransactionValidationException("师徽纹理状态不是直接MAP");
        var normal = doc.ReadMapEntries(entry.Value).Where(e => doc.Raw(e.Key) == "~/ComponentState/Normal").ToArray();
        if (normal.Length != 1) throw new TransactionValidationException("师徽Normal状态缺失或重复");
        return doc.Raw(normal[0].Value);
    }

    private static void Register(string root, string path, string key, string referenceKey, List<PlannedFileChange> files, bool allowExisting)
    {
        var graph = new UnitProjectGraph(root, files);
        if (graph.FindObjects(key).Count != 1) throw new TransactionValidationException("师徽纹理声明重复：" + key);
        var texture = graph.RequireObject(path, key);
        var banks = Banks(graph, key, referenceKey, path);
        var existing = banks.SelectMany(b => b.Entries.Where(e => NdfSyntaxDocument.Unquote(b.File.Syntax.Raw(e.Key)) == key).Select(e => (Bank: b, Entry: e))).ToArray();
        if (existing.Length > 0)
        {
            if (!allowExisting || existing.Length != 1) throw new TransactionValidationException("师徽纹理库键重复或已占用：" + key);
            if (!UnitProjectGraph.Same(graph.Resolve(existing[0].Bank.File.Path, Normal(existing[0].Bank, existing[0].Entry)), texture))
                throw new TransactionValidationException("师徽纹理库键指向其他资源：" + key);
            return;
        }
        var sourceEntries = banks.SelectMany(b => b.Entries.Where(e => referenceKey.Length > 0 && NdfSyntaxDocument.Unquote(b.File.Syntax.Raw(e.Key)) == referenceKey).Select(e => (Bank: b, Entry: e))).ToArray();
        Bank bank; string raw;
        if (sourceEntries.Length > 0)
        {
            if (sourceEntries.Length != 1) throw new TransactionValidationException("来源师徽纹理库键重复：" + referenceKey);
            bank = sourceEntries[0].Bank;
            var sourceRaw = Normal(bank, sourceEntries[0].Entry); var source = graph.Resolve(bank.File.Path, sourceRaw);
            if (source is null || source.Name != referenceKey || source.TypeName != "TUIResourceTexture_Common" || !UnitProjectGraph.Normalize(source.RelativeSourceFile).Equals(UnitProjectGraph.Normalize(path), StringComparison.OrdinalIgnoreCase))
                throw new TransactionValidationException("来源师徽纹理库关联无法确认");
            raw = sourceRaw[..(sourceRaw.Length - source.Name.Length)] + key;
        }
        else
        {
            // Legacy missing keys: use a unique existing bank with verified texture bindings in this declaration file.
            var candidates = banks.Where(b => b.File.Path.Equals(UnitProjectGraph.Normalize(path), StringComparison.OrdinalIgnoreCase) && b.Entries.Any(e =>
            {
                var entryKey = NdfSyntaxDocument.Unquote(b.File.Syntax.Raw(e.Key));
                if (!entryKey.StartsWith("Texture_Division_Emblem_", StringComparison.Ordinal)) return false;
                var target = graph.Resolve(b.File.Path, Normal(b, e));
                return target is not null && target.Name == entryKey && target.TypeName == "TUIResourceTexture_Common" && UnitProjectGraph.Normalize(target.RelativeSourceFile).Equals(b.File.Path, StringComparison.OrdinalIgnoreCase);
            })).ToArray();
            if (candidates.Length != 1) throw new TransactionValidationException("无法唯一确定师徽纹理库，请检查现有师徽注册");
            bank = candidates[0]; raw = "~/" + key;
        }
        if (!UnitProjectGraph.Same(graph.Resolve(bank.File.Path, raw), texture)) throw new TransactionValidationException("新增师徽纹理引用作用域不匹配");
        var doc = bank.File.Syntax; var at = doc.Tokens[bank.Map.EndTokenIndex].Start;
        var nl = bank.File.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lineStart = bank.File.Text.LastIndexOf('\n', at) + 1;
        var closingIndent = bank.File.Text[lineStart..at];
        var ownLine = closingIndent.All(c => c is ' ' or '\t');
        var indent = ownLine ? closingIndent + (closingIndent.Contains('\t') ? "\t" : "    ") : "        ";
        if (ownLine) at = lineStart;
        var insertion = (doc.NeedsArraySeparator(bank.Map) ? "," + nl : ownLine ? "" : nl) + indent + "(\"" + key + "\", MAP [(~/ComponentState/Normal, " + raw + ")])," + nl;
        var candidate = bank.File.Text.Insert(at, insertion);
        UnitProjectGraph.Put(root, bank.File.Path, candidate, files, allowExisting ? "补登记已有自定义师徽纹理" : "登记新增师徽纹理");
        var final = new UnitProjectGraph(root, files);
        var matches = Banks(final, key, referenceKey, path).SelectMany(b => b.Entries.Where(e => NdfSyntaxDocument.Unquote(b.File.Syntax.Raw(e.Key)) == key).Select(e => (Bank: b, Entry: e))).ToArray();
        if (matches.Length != 1 || !UnitProjectGraph.Same(final.Resolve(matches[0].Bank.File.Path, Normal(matches[0].Bank, matches[0].Entry)), final.RequireObject(path, key)))
            throw new TransactionValidationException("最终师徽纹理库注册校验失败");
    }
}
