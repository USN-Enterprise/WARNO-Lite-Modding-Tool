using System.Text.Json;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Localisation;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Divisions;

public sealed record DivisionTextPart(string Field, string Token, string Raw, string? Text, string Source, string Error);
public sealed record DivisionTextPayload(int Version, string Token, string Text, string CsvPath, string? ModBaseline);

public static class DivisionText
{
    public static readonly string[] Fields = ["SummaryTextToken", "HistoryTextToken"];
    public static string Label(string field) => field == Fields[0] ? "游戏玩法简介" : "历史介绍";
    public static DivisionTextPayload Payload(DraftOperation op) => JsonSerializer.Deserialize<DivisionTextPayload>(op.TargetRaw) ?? throw new InvalidDataException("师正文草稿为空");
    public static DivisionTextPart Read(DivisionWorkspaceData data, DivisionRecord division, string field, string language)
    {
        var token = ""; var raw = "";
        try
        {
            if (!Fields.Contains(field)) throw new InvalidDataException("不支持的师正文字段");
            var doc = new NdfSyntaxDocument(DivisionIdentity.Source(division));
            var span = doc.FindDirectAssignments(doc.FindConstructors("TDeckDivisionDescriptor").Single(), field).Single();
            raw = doc.Raw(span);
            if (raw.Length < 2 || raw[0] is not ('\'' or '"') || raw[^1] != raw[0]) throw new InvalidDataException("正文引用不是支持的字符串");
            token = NdfSyntaxDocument.Unquote(raw);
            var local = data.Units.Localisation;
            if (local.IsTokenAmbiguous(token)) throw new InvalidDataException("当前Mod正文token重复");
            if (local.TryResolve(token, out var text)) return new(field, token, raw, text, "当前Mod默认正文", "");
            var original = VanillaNames.Lookup("UNITS", token, language);
            var fallback = language == "SC" ? "US" : "SC";
            return new(field, token, raw, original ?? VanillaNames.Lookup("UNITS", token, fallback),
                original is not null ? "原版" + language : VanillaNames.Lookup("UNITS", token, fallback) is not null ? "原版" + fallback + "（回退）" : "来源未解析", "");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
        { return new(field, token, raw, null, "来源未解析", ex.Message); }
    }
    public static DraftOperation Operation(DivisionWorkspaceData data, DivisionRecord division, DivisionTextPart baseline,
        string text, IReadOnlyList<DraftOperation> drafts, DraftOperation? existing = null)
    {
        if (baseline.Error.Length > 0) throw new InvalidDataException(baseline.Error);
        ValidateText(text); VanillaNames.RequireUnitsAvailable();
        var csv = data.Units.Localisation.UniqueUnitsCsvPath ?? throw new InvalidDataException("无法唯一定位UNITS.csv");
        if (data.Units.Localisation.Diagnostics.Count > 0) throw new InvalidDataException("请先处理本地化声明或CSV诊断");
        var path = Path.GetRelativePath(data.ProjectRoot, csv).Replace('\\', '/');
        var id = DraftOperation.CreateId(DraftTargetKind.DivisionText, division.Source.RelativeSourceFile, division.Name, baseline.Field);
        if (existing is not null && (existing.Id != id || Resolve(data, existing).Status != DraftResolutionStatus.Active)) throw new InvalidDataException("师正文草稿冲突，请先撤销或重新加载");
        string token;
        if (existing is not null) token = Payload(existing).Token;
        else
        {
            var occupied = new HashSet<string>(drafts.Select(o => o.NameToken).OfType<string>(), StringComparer.Ordinal);
            var graph = new UnitProjectGraph(data.ProjectRoot);
            foreach (var t in graph.Files.Values.SelectMany(f => f.Syntax.Tokens)) occupied.Add(NdfSyntaxDocument.Unquote(t.Text));
            do token = "WL" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            while (occupied.Contains(token) || VanillaNames.Lookup("UNITS", token, "US") is not null || VanillaNames.Lookup("UNITS", token, "SC") is not null ||
                data.Units.Localisation.TryResolve(token, out _) || data.Units.Localisation.IsTokenAmbiguous(token));
        }
        var hasModText = data.Units.Localisation.TryResolve(baseline.Token, out var modText);
        var payload = new DivisionTextPayload(1, token, text, path, hasModText ? modText : null);
        return new(id, "division-text:" + division.Name, DraftTargetKind.DivisionText, "divisions", division.Source.RelativeSourceFile,
            division.Name, "TDeckDivisionDescriptor", baseline.Field, baseline.Field, "DivisionText", baseline.Text ?? "（来源未解析）", baseline.Raw,
            text, JsonSerializer.Serialize(payload), division.DisplayName + " · " + Label(baseline.Field), token, true, DateTimeOffset.UtcNow, baseline.Token);
    }
    private static void ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("本版不支持清空正文，请输入非空内容");
        if (text.Contains('\0')) throw new InvalidDataException("正文不能包含空字符");
        _ = new System.Text.UTF8Encoding(false, true).GetBytes(text);
    }
    public static ResolvedDraftOperation Resolve(DivisionWorkspaceData? data, DraftOperation op)
    {
        try
        {
            var division = data?.Division(op.ObjectName) ?? throw new InvalidDataException("目标师不存在");
            var p = Payload(op); ValidateText(p.Text);
            if (p.Version != 1 || op.ObjectType != "TDeckDivisionDescriptor" || !Fields.Contains(op.FieldKey) || p.Token != op.NameToken ||
                !Regex.IsMatch(p.Token, "^[A-Z0-9]{10}$") || p.Token == op.BaselineNameToken || op.RelativeSourceFile != division.Source.RelativeSourceFile)
                throw new InvalidDataException("师正文草稿身份无效");
            var current = Read(data!, division, op.FieldKey, "US");
            if (current.Error.Length > 0 || current.Raw != op.BaselineRaw || current.Token != op.BaselineNameToken) throw new InvalidDataException("师正文引用基线已变化：" + current.Error);
            var local = data!.Units.Localisation;
            if (local.Diagnostics.Count > 0 || local.UniqueUnitsCsvPath is null || Path.GetRelativePath(data.ProjectRoot, local.UniqueUnitsCsvPath).Replace('\\', '/') != p.CsvPath)
                throw new InvalidDataException("正文词典声明不可用或已变化");
            var has = local.TryResolve(current.Token, out var value);
            if (local.IsTokenAmbiguous(current.Token) || (has ? value : null) != p.ModBaseline) throw new InvalidDataException("当前Mod原正文已变化");
            return new(op, DraftResolutionStatus.Active, "");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException or JsonException)
        { return new(op, DraftResolutionStatus.Conflict, ex.Message); }
    }
    public static void Plan(string root, DivisionWorkspaceData? data, IReadOnlyList<DraftOperation> operations,
        IReadOnlyList<DraftOperation> allDrafts, List<PlannedFileChange> files)
    {
        var ops = operations.Where(o => o.TargetKind == DraftTargetKind.DivisionText).ToArray();
        if (ops.Length == 0) return;
        VanillaNames.RequireUnitsAvailable();
        var candidate = new CandidateTextFiles(root, files);
        var graph = new UnitProjectGraph(root, files);
        var used = graph.Files.Values.SelectMany(f => f.Syntax.Tokens).Select(t => NdfSyntaxDocument.Unquote(t.Text)).ToHashSet(StringComparer.Ordinal);
        foreach (var op in ops)
        {
            var resolved = Resolve(data, op);
            if (resolved.Status != DraftResolutionStatus.Active) throw new TransactionValidationException(resolved.Reason);
            var p = Payload(op);
            if (!used.Add(p.Token) || allDrafts.Concat(operations).Any(o => o.Id != op.Id && o.NameToken == p.Token) ||
                VanillaNames.Lookup("UNITS", p.Token, "US") is not null || VanillaNames.Lookup("UNITS", p.Token, "SC") is not null)
                throw new TransactionValidationException("师正文token已占用");
            var csv = candidate.Get(p.CsvPath, FormalTextFileKind.Csv);
            if (csv.Length == 0) csv = "TOKEN;REFTEXT";
            var parsed = SemicolonCsvDocument.Parse(csv);
            if (parsed.Rows.Count == 0 || parsed.Rows[0].Fields.Count != 2 || parsed.Rows[0].Fields[0].Value.Trim() != "TOKEN" || parsed.Rows[0].Fields[1].Value.Trim() != "REFTEXT" ||
                parsed.Rows.Skip(1).Any(r => r.Fields.Count != 2 || r.Fields[0].Value.Trim() == p.Token)) throw new TransactionValidationException("正文CSV格式或token冲突");
            var nl = candidate.NewLine(p.CsvPath);
            var newCsv = csv + (csv.EndsWith('\n') ? "" : nl) + p.Token + ";" + SemicolonCsvDocument.Quote(p.Text) + nl;
            var check = SemicolonCsvDocument.Parse(newCsv).Rows.Last();
            if (check.Fields.Count != 2 || check.Fields[1].Value != p.Text) throw new TransactionValidationException("正文CSV回读失败");
            candidate.Set(p.CsvPath, newCsv, FormalTextFileKind.Csv);
            var source = candidate.Get(op.RelativeSourceFile);
            var objects = new NdfTopLevelScanner().Scan(source, Path.Combine(root, op.RelativeSourceFile), "divisions", root);
            var obj = objects.Objects.Single(o => o.Name == op.ObjectName && o.TypeName == "TDeckDivisionDescriptor");
            var doc = new NdfSyntaxDocument(source, obj.CharacterOffset, obj.CharacterLength);
            var span = doc.FindDirectAssignments(doc.FindConstructors("TDeckDivisionDescriptor").Single(), op.FieldKey).Single();
            if (doc.Raw(span) != op.BaselineRaw) throw new TransactionValidationException("师正文候选引用冲突");
            var result = source.Remove(doc.StartOffset(span), doc.Length(span)).Insert(doc.StartOffset(span), "'" + p.Token + "'");
            candidate.Set(op.RelativeSourceFile, result);
        }
        candidate.Complete("当前师独立简介/历史正文；默认文本不自动翻译，旧词条保留");
    }
}
