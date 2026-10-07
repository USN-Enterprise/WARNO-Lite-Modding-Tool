using System.Text;
using WarnoLiteModdingTool.Core.Changes;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.SourceWorkspace;

public static class SourceValidation
{
    public static void KnownCombinations(string root, IReadOnlyList<PlannedFileChange> files, IReadOnlyList<WarnoLiteModdingTool.Core.Drafts.DraftOperation> operations, ICollection<string> messages, CancellationToken token)
    {
        var final = files.Select(f => new RestoreFile(f.RelativePath, !f.Existed ? null : SourceFiles.IsNdf(f.RelativePath) ? Encoding.UTF8.GetBytes(SourceFiles.Open(root,f.RelativePath,operations.Single(o=>o.RelativeSourceFile.Equals(f.RelativePath,StringComparison.OrdinalIgnoreCase)).Source!.CodePage).Text) : f.OriginalBytes, f.CandidateBytes,"source",[])).ToList();
        var sharedHeight = files.Any(f => f.RelativePath.Equals("GameData/Gameplay/Unit/AirplaneConstantes.ndf",StringComparison.OrdinalIgnoreCase) || f.RelativePath.Equals("GameData/Gameplay/Unit/Tactic/ModulesStandard.ndf",StringComparison.OrdinalIgnoreCase));
        if (sharedHeight)
        {
            foreach (var entry in SourceFiles.Discover(root,token).Files.Where(f => SourceFiles.IsNdf(f.Path) && final.All(c => !c.Path.Equals(f.Path,StringComparison.OrdinalIgnoreCase))))
            {
                token.ThrowIfCancellationRequested();
                try { var bytes = SourceFiles.Read(root,entry.Path); if (bytes is not null && Encoding.UTF8.GetString(bytes).Contains("TEntityDescriptor",StringComparison.Ordinal)) final.Add(new(entry.Path,bytes,bytes,"dependency",[])); }
                catch (IOException ex) { messages.Add(entry.Path + " · 航空关联未检查 / Aviation dependency unchecked: " + ex.Message); }
            }
        }
        token.ThrowIfCancellationRequested();
        try { AviationMovement.ValidateRestored(root,final,reviewAllHeights:sharedHeight); messages.Add("已执行已知航空组合规则（仅适用的可识别对象）；其余语义未验证 / Known aviation rules run for applicable recognized objects only; other semantics unverified"); }
        catch (TransactionValidationException ex) when (ex.Code == "aviation.unsupported") { messages.Add("航空结构超出支持范围 / Aviation structure unverified: " + ex.Message); }
        catch (TransactionValidationException) { throw; }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { messages.Add("航空关联未完整检查 / Aviation dependencies not fully checked: " + ex.Message); }
    }
    public static void CheckLexicalDelta(string path, string before, string after)
    {
        string Rule(string message) => message[(message.IndexOf(':') + 1)..].Trim();
        var existing = SourceLanguage.Diagnostics(before).GroupBy(Rule).ToDictionary(g => g.Key,g => g.Count());
        var introduced = SourceLanguage.Diagnostics(after).GroupBy(Rule).Where(g => g.Count() > existing.GetValueOrDefault(g.Key)).SelectMany(g=>g).ToArray();
        if (introduced.Length > 0) throw new InvalidDataException(path + "\n" + string.Join("\n",introduced));
    }
}
