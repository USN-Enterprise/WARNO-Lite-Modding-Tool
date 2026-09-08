using System.Globalization;
using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Localisation;
namespace WarnoLiteModdingTool.Core.Rules;

public sealed record RuleCell(string Key, string Label, string Raw, int Offset, int Length, bool Boolean, bool Integer);
public sealed record RuleGroup(RuleDefinition Definition, IReadOnlyList<RuleCell> Cells, string Baseline, string Error)
{
    public bool CanEdit => Error.Length == 0 && Cells.Count > 0;
}
public sealed class RuleWorkspace(string root, IReadOnlyList<RuleGroup> groups)
{
    public string Root { get; } = root;
    public IReadOnlyList<RuleGroup> Groups { get; } = groups;
    public static RuleWorkspace Load(string root)
    {
        var sources = new Dictionary<string,string>();
        var groups = new List<RuleGroup>();
        foreach (var definition in RuleCatalog.All)
        {
            try
            {
                if (!sources.TryGetValue(definition.RelativePath, out var source))
                {
                    var snapshot = TextFileSnapshot.Load(root, definition.RelativePath, FormalTextFileKind.Ndf);
                    if (!snapshot.Existed) throw new InvalidDataException("当前 Mod 缺少此文件");
                    source = snapshot.Text; sources[definition.RelativePath] = source;
                }
                groups.Add(Read(definition, source));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
            { groups.Add(new(definition, [], "", ex.Message)); }
        }
        return new(root, groups);
    }
    public static NdfValueSpan Locate(NdfSyntaxDocument doc, string type, string field)
    {
        var spans = type.Length == 0 ? doc.FindNamedValues(field) : doc.FindConstructors(type).SelectMany(c => doc.FindDirectAssignments(c, field)).ToArray();
        return spans.Count == 1 ? spans[0] : throw new InvalidDataException("缺少唯一字段：" + field);
    }
    public static RuleGroup Read(RuleDefinition definition, string source)
    {
        var doc = new NdfSyntaxDocument(source); var cells = new List<RuleCell>(); var baseline = new List<string>();
        foreach (var field in definition.Fields.Split(';'))
        {
            var span = Locate(doc, definition.Constructor, field);
            if (definition.MapKey is {} mapKey)
            {
                var entries = doc.ReadMapEntries(span).Where(e => doc.Raw(e.Key) == mapKey).ToArray();
                if (entries.Length != 1) throw new InvalidDataException("缺少唯一选项：" + mapKey);
                span = entries[0].Value;
            }
            baseline.Add(doc.Raw(span));
            Visit(span, field, definition.Fields.Contains(';') ? CellLabel(field) : "值");
        }
        if (cells.Count == 0 || cells.Select(c=>c.Key).Distinct(StringComparer.Ordinal).Count()!=cells.Count) throw new InvalidDataException("未找到唯一可编辑数值");
        return new(definition, cells, JsonSerializer.Serialize(baseline), "");
        void Visit(NdfValueSpan value, string key, string label)
        {
            var raw = doc.Raw(value).Trim();
            if (raw.StartsWith("MAP"))
            {
                var entries = doc.ReadMapEntries(value);
                if (entries.Count == 0) throw new InvalidDataException("空 MAP 无法编辑");
                foreach (var entry in entries) Visit(entry.Value, key + "/" + doc.Raw(entry.Key), CellLabel(doc.Raw(entry.Key)));
            }
            else if (raw.StartsWith('['))
            {
                var entries = doc.ReadArrayElements(value);
                for (var i = 0; i < entries.Count; i++)
                {
                    var title = definition.Number == 33 ? new[]{"战斗", "辅助支援", "空中支援", "地面支援"}.ElementAtOrDefault(i) ?? i.ToString() : label + " · " + i;
                    if (definition.Number is 34 or 35 && key == definition.Fields) title = i == 0 ? "攻方 · 参战数量" : "守方 · 参战数量";
                    Visit(entries[i], key + "/" + i, title);
                }
            }
            else
            {
                var boolean = definition.Number == 46 && raw is "true" or "false";
                if (!boolean && !decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) throw new InvalidDataException("不是直接数值：" + label);
                cells.Add(new(key, label, raw, doc.StartOffset(value), doc.Length(value), boolean, !boolean && definition.Number is 1 or 2 or 3 or 4 or 5 or 6 or 16 or 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40 or 43 or 44 or 45 or 47 or 48 or 50));
            }
        }
    }
    public static string CellLabel(string key) => key switch
    {
        "EIADifficulty/TresFacile"=>"非常简单", "EIADifficulty/Facile"=>"简单", "EIADifficulty/Normal"=>"普通", "EIADifficulty/Difficile"=>"困难", "EIADifficulty/TresDifficile"=>"非常困难", "EIADifficulty/PlusDifficile"=>"极难",
        "EVictoryType/TotalDefeat"=>"惨败", "EVictoryType/MajorDefeat"=>"重大失败", "EVictoryType/MinorDefeat"=>"小败", "EVictoryType/Draw"=>"平局", "EVictoryType/MinorVictory"=>"小胜", "EVictoryType/MajorVictory"=>"重大胜利", "EVictoryType/TotalVictory"=>"全面胜利", "EVictoryType/NotSpecified"=>"未指定战果",
        "UpkeepPercentAvailableSettings"=>"维护费比例选项", "UpkeepPercentDefaultSetting"=>"默认维护费比例", _=>key
    };
    public DraftOperation Operation(RuleGroup group, IReadOnlyDictionary<string,string> values)
    {
        Validate(group, values);
        var d = group.Definition; var key = d.Number.ToString(CultureInfo.InvariantCulture);
        var json = JsonSerializer.Serialize(values);
        var changes = group.Cells.Where(c => c.Raw != values[c.Key]).Select(c => c.Label + "：" + c.Raw + " → " + values[c.Key]);
        return new(DraftOperation.CreateId(DraftTargetKind.GlobalRule, d.RelativePath, d.Constructor, key), "rules:" + key, DraftTargetKind.GlobalRule,
            "rules", d.RelativePath, d.Constructor, "GlobalRule", key, d.Fields, "NumericCells", string.Join("；", group.Cells.Select(c=>c.Label + "：" + c.Raw)), group.Baseline, string.Join("；",group.Cells.Select(c=>c.Label + "：" + values[c.Key])), json,
            d.Number + ". " + d.Label + " · " + string.Join("；", changes) + (d.Number==33 ? "；自动调整四张参战关联表长度，新增格沿用末格值" : d.Number==2 ? "；新增资金档同步补充歼灭分数映射，旧档映射保留" : ""), null, false, DateTimeOffset.UtcNow);
    }
    public static Dictionary<string,string> Values(DraftOperation operation) => JsonSerializer.Deserialize<Dictionary<string,string>>(operation.TargetRaw) ?? throw new InvalidDataException("草稿为空");
    public static void Validate(RuleGroup group, IReadOnlyDictionary<string,string> values)
    {
        if (!group.CanEdit || values.Count != group.Cells.Count || group.Cells.Any(c => !values.ContainsKey(c.Key))) throw new InvalidDataException("数值格结构已变化");
        foreach (var cell in group.Cells)
        {
            var raw = values[cell.Key];
            if (cell.Boolean) { if (raw is not ("true" or "false")) throw new InvalidDataException("请选择是或否"); continue; }
            if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || (cell.Integer && !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) || (group.Definition.Number != 16 && value < 0))
                throw new InvalidDataException(cell.Label + "：请输入" + (cell.Integer ? "整数" : "数值") + (group.Definition.Number != 16 ? "（不小于 0）" : ""));
            if (group.Definition.Number == 33 && value > 64) throw new InvalidDataException("参战名额总数不能超过 64");
            if (group.Definition.Number == 42 && value > 1) throw new InvalidDataException("解散阈值应在 0 到 1 之间");
        }
    }
    public ResolvedDraftOperation Resolve(DraftOperation operation)
    {
        try
        {
            var group = Groups.Single(g => g.Definition.Number.ToString(CultureInfo.InvariantCulture) == operation.FieldKey);
            if (operation.Module != "rules" || operation.RelativeSourceFile != group.Definition.RelativePath || operation.ObjectName != group.Definition.Constructor || operation.ObjectType != "GlobalRule" || operation.BaselineRaw != group.Baseline || operation.Id != DraftOperation.CreateId(DraftTargetKind.GlobalRule, group.Definition.RelativePath, group.Definition.Constructor, operation.FieldKey))
                throw new InvalidDataException("规则身份或文件基线已变化");
            Validate(group, Values(operation));
            return new(operation, DraftResolutionStatus.Active, "");
        }
        catch(Exception ex) when(ex is InvalidOperationException or InvalidDataException or IOException or JsonException or ArgumentException)
        { return new(operation, DraftResolutionStatus.Conflict, ex.Message); }
    }
    public void Plan(IReadOnlyList<DraftOperation> operations, List<PlannedFileChange> planned)
    {
        var active = operations.Where(o => o.TargetKind == DraftTargetKind.GlobalRule).ToArray();
        foreach(var file in active.GroupBy(o => o.RelativeSourceFile))
        {
            var snapshot = TextFileSnapshot.Load(Root, file.Key, FormalTextFileKind.Ndf);
            var replacements = new List<TextReplacement>();
            foreach (var op in file)
            {
                var result = Resolve(op);
                if (result.Status == DraftResolutionStatus.Conflict) throw new TransactionValidationException(result.Reason);
                var group = Read(Groups.Single(g => g.Definition.Number.ToString() == op.FieldKey).Definition, snapshot.Text);
                var values = Values(op);
                foreach (var cell in group.Cells.Where(c => c.Raw != values[c.Key]))
                    replacements.Add(new(cell.Offset, cell.Length, cell.Raw, values[cell.Key], op.Summary));
            }
            var candidate = SemicolonCsvDocument.ApplyReplacements(snapshot.Text, replacements);
            try { candidate = RuleDependencies.AdjustAndValidate(file.Key, snapshot.Text, candidate, file.Select(o => int.Parse(o.FieldKey)).ToHashSet()); }
            catch(Exception ex) when(ex is InvalidDataException or FormatException or OverflowException)
            { throw new TransactionValidationException("关联规则校验失败：" + ex.Message); }
            planned.Add(new(snapshot.RelativePath, snapshot.FullPath, FormalTextFileKind.Ndf, PlannedFileAction.Write, true, snapshot.OriginalBytes, snapshot.Encode(candidate), snapshot.LastWriteUtc, file.Select(o => o.Summary).ToArray()));
        }
    }
}
