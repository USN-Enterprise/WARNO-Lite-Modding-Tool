using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Rules;

public sealed record ExperienceCell(string Key, string Label, string Field, string Raw, string File,
    int Offset, int Length, string Source, bool Nonnegative, string Error = "");
public sealed record ExperienceLevel(int Index, IReadOnlyList<ExperienceCell> Cells, string Baseline,
    IReadOnlyList<string> Notes, string Error);
public sealed record ExperienceRoute(string Name, string File, string Alias, IReadOnlyList<ExperienceLevel> Levels,
    IReadOnlyList<string> Users, string Error);

/// <summary>Edits existing numeric fields only. Identity, arrays, effect semantics and references are preserved.</summary>
public sealed class ExperienceWorkspace
{
    private sealed record Declaration(string Name, string Type, string File, string Text, int Offset, bool Valid);
    private sealed record Reference(string Target, string Owner, string File, int Offset, string Raw);
    private readonly Dictionary<string, List<Declaration>> _objects = new(StringComparer.Ordinal);
    private readonly List<Reference> _references = [];
    private readonly Dictionary<string, List<Reference>> _incoming = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sources = new(StringComparer.OrdinalIgnoreCase);
    public List<ExperienceRoute> Routes { get; } = [];
    public List<string> Diagnostics { get; } = [];
    public string Root { get; }
    private ExperienceWorkspace(string root) => Root = root;
    public static string Alias(string name) => name switch
    {
        "ExperienceLevelsPackDescriptor_XP_pack_simple_v3" => "普通",
        "ExperienceLevelsPackDescriptor_XP_pack_SF_v2" => "特种经验",
        "ExperienceLevelsPackDescriptor_XP_pack_artillery" => "火炮",
        "ExperienceLevelsPackDescriptor_XP_pack_helico" => "直升机",
        "ExperienceLevelsPackDescriptor_XP_pack_avion" => "固定翼",
        _ => "自定义"
    };
    public static ExperienceWorkspace Load(string root, IReadOnlyDictionary<string, string>? candidates = null)
    {
        var result = new ExperienceWorkspace(root);
        var directory = Path.Combine(root, "GameData");
        if (!Directory.Exists(directory)) return result;
        var paths = Directory.EnumerateFiles(directory, "*.ndf", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .Concat(candidates?.Keys.Where(p => p.StartsWith("GameData/", StringComparison.OrdinalIgnoreCase)) ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        foreach (var relative in paths)
        {
            try { result._sources[relative] = candidates?.GetValueOrDefault(relative) ?? File.ReadAllText(Path.Combine(root, relative)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { result.Diagnostics.Add(relative + ": " + ex.Message); }
        }
        if (!result._sources.Values.Any(s => s.Contains("TExperienceLevelsPackDescriptor", StringComparison.Ordinal)))
        { result._sources.Clear(); return result; }
        foreach (var (relative, text) in result._sources)
        {
            var parsed = MaskBlockComments(text);
            var scan = new NdfTopLevelScanner().Scan(parsed, Path.Combine(root, relative), "rules", root);
            foreach (var o in scan.Objects)
            {
                var item = new Declaration(o.Name, o.TypeName, relative, text.Substring(o.CharacterOffset, o.CharacterLength),
                    o.CharacterOffset, !scan.Diagnostics.Any(d => d.Severity == NdfDiagnosticSeverity.Error));
                if (!result._objects.TryGetValue(o.Name, out var declarations)) result._objects[o.Name] = declarations = [];
                declarations.Add(item);
            }
            var doc = new NdfSyntaxDocument(parsed);
            var objectIndex = 0;
            foreach (var r in doc.FindReferences("").Where(r => r.Raw.StartsWith("~/") || r.Raw.StartsWith("$/")))
            {
                var offset = doc.StartOffset(r.Span);
                while (objectIndex < scan.Objects.Count && scan.Objects[objectIndex].CharacterOffset + scan.Objects[objectIndex].CharacterLength <= offset) objectIndex++;
                var owner = objectIndex < scan.Objects.Count && scan.Objects[objectIndex].CharacterOffset <= offset
                    ? scan.Objects[objectIndex].Name : "@" + relative;
                result._references.Add(new(r.Leaf, owner, relative, offset, r.Raw));
            }
        }
        foreach (var reference in result._references)
        {
            if (!result._incoming.TryGetValue(reference.Target, out var incoming)) result._incoming[reference.Target] = incoming = [];
            incoming.Add(reference);
        }
        foreach (var entries in result._objects.Values.Where(list => list.Any(d => d.Type == "TExperienceLevelsPackDescriptor")))
        {
            var route = result.ReadRoute(entries);
            result.Routes.Add(result.Diagnostics.Count == 0 ? route : route with { Error = "部分文件无法读取，共享影响无法完整核对" });
        }
        var order = new[] { "普通", "特种经验", "火炮", "直升机", "固定翼", "自定义" };
        result.Routes.Sort((a, b) => Array.IndexOf(order, a.Alias) != Array.IndexOf(order, b.Alias)
            ? Array.IndexOf(order, a.Alias).CompareTo(Array.IndexOf(order, b.Alias)) : string.CompareOrdinal(a.Name, b.Name));
        return result;
    }
    private static string MaskBlockComments(string text) => Regex.Replace(text,
        "//[^\\r\\n]*|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|/\\*[\\s\\S]*?\\*/",
        m => m.Value.StartsWith("/*", StringComparison.Ordinal) ? new string(m.Value.Select(c => c is '\r' or '\n' ? c : ' ').ToArray()) : m.Value);

    private IReadOnlyList<Reference> Impact(string target)
    {
        var found = new HashSet<Reference>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(); pending.Enqueue(target);
        while (pending.TryDequeue(out var name))
        {
            if (!visited.Add(name)) continue;
            foreach (var r in _incoming.GetValueOrDefault(name) ?? [])
            { found.Add(r); pending.Enqueue(r.Owner); }
        }
        return found.OrderBy(r => r.File, StringComparer.Ordinal).ThenBy(r => r.Owner, StringComparer.Ordinal)
            .ThenBy(r => r.Target, StringComparer.Ordinal).ThenBy(r => r.Offset).ToArray();
    }
    private ExperienceRoute ReadRoute(List<Declaration> entries)
    {
        var route = entries[0];
        var impact = Impact(route.Name);
        var users = impact.Select(r => r.Owner + " · " + r.File).Distinct().ToArray();
        if (entries.Count != 1 || !route.Valid)
            return new(route.Name, route.File, Alias(route.Name), [], users, "路线声明多义或源文件语法错误");
        var doc = new NdfSyntaxDocument(MaskBlockComments(route.Text));
        var top = doc.FindConstructors("TExperienceLevelsPackDescriptor");
        var arrays = top.Count == 1 ? doc.FindDirectAssignments(top[0], "ExperienceLevelsDescriptors") : [];
        if (arrays.Count != 1 || !doc.Raw(arrays[0]).TrimStart().StartsWith('['))
            return new(route.Name, route.File, Alias(route.Name), [], users, "无法唯一定位等级数组");
        var levels = new List<ExperienceLevel>();
        var elements = doc.ReadArrayElements(arrays[0]);
        for (var i = 0; i < elements.Count; i++)
        {
            var cells = new List<ExperienceCell>(); var notes = new List<string>(); var dependencies = new List<string> { route.Text };
            var constructors = doc.FindConstructors("TExperienceLevelDescriptor", elements[i]);
            var error = "";
            if (constructors.Count != 1 || constructors[0].TypeTokenIndex != elements[i].StartTokenIndex ||
                constructors[0].CloseTokenIndex != elements[i].EndTokenIndex) error = "不支持的等级结构";
            else
            {
                var level = constructors[0];
                AddCell(route, doc, level, "ThresholdAdditionalValue", "门槛加值", "threshold.add", true, cells, notes);
                AddCell(route, doc, level, "ThresholdPriceMultiplier", "门槛价格系数", "threshold.price", true, cells, notes);
                var effectArrays = doc.FindDirectAssignments(level, "LevelEffectsPacks");
                if (effectArrays.Count == 0) notes.Add("未显式配置等级效果");
                else if (effectArrays.Count != 1 || !doc.Raw(effectArrays[0]).TrimStart().StartsWith('[')) error = "等级效果赋值不唯一或结构不支持";
                else foreach (var effect in doc.ReadArrayElements(effectArrays[0]))
                {
                    var raw = doc.Raw(effect).Trim(); var name = NdfSyntaxDocument.Leaf(raw);
                    if (!(raw == "~/" + name || raw == "$/GFX/EffectCapacity/" + name) ||
                        !_objects.TryGetValue(name, out var declarations) || declarations.Count != 1 || declarations[0].Type != "TEffectsPackDescriptor" || !declarations[0].Valid)
                    { error = "等级效果引用缺失、多义或不支持：" + raw; continue; }
                    var descriptor = declarations[0];
                    dependencies.Add(descriptor.File + "\n" + descriptor.Text);
                    var incoming = _incoming.GetValueOrDefault(name) ?? [];
                    // First release edits exclusively referenced effects. Shared effects stay visible and read-only.
                    var shared = incoming.Count != 1 || incoming[0].Owner != route.Name;
                    var reason = shared ? "效果被多处引用，当前版本仅支持独占效果数值编辑" : "";
                    if (shared) notes.Add(reason + "：" + name + "\n" + string.Join("\n", Impact(name).Select(r => r.Owner + " · " + r.File).Distinct()));
                    ReadEffects(descriptor, cells, notes, reason);
                    dependencies.Add(JsonSerializer.Serialize(Impact(name).Select(r => new { r.File, r.Owner, r.Target, r.Raw })));
                }
            }
            dependencies.Add(JsonSerializer.Serialize(impact.Select(r => new { r.File, r.Owner, r.Target, r.Raw })));
            levels.Add(new(i, cells, JsonSerializer.Serialize(dependencies), notes, error));
        }
        return new(route.Name, route.File, Alias(route.Name), levels, users, elements.Count == 0 ? "等级数组为空" : "");
    }
    private static void AddCell(Declaration source, NdfSyntaxDocument doc, NdfConstructorSpan constructor,
        string field, string label, string key, bool nonnegative, List<ExperienceCell> cells, List<string> notes, string error = "")
    {
        var assignments = doc.FindDirectAssignments(constructor, field);
        if (assignments.Count != 1) { notes.Add(field + "：未配置或赋值不唯一"); return; }
        var value = assignments[0]; var raw = doc.Raw(value);
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || nonnegative && number < 0)
            error = "字段不是支持的直接数值";
        cells.Add(new(key, label, field, raw, source.File, source.Offset + doc.StartOffset(value), doc.Length(value), source.Name, nonnegative, error));
    }
    private static void ReadEffects(Declaration source, List<ExperienceCell> cells, List<string> notes, string sharedError)
    {
        var doc = new NdfSyntaxDocument(MaskBlockComments(source.Text));
        var top = doc.FindConstructors("TEffectsPackDescriptor");
        var arrays = top.Count == 1 ? doc.FindDirectAssignments(top[0], "EffectsDescriptors") : [];
        if (arrays.Count != 1 || !doc.Raw(arrays[0]).TrimStart().StartsWith('[')) { notes.Add("效果列表无法编辑：" + source.Name); return; }
        var elements = doc.ReadArrayElements(arrays[0]);
        if (elements.Count == 0) notes.Add("空效果包：" + source.Name);
        for (var i = 0; i < elements.Count; i++)
        {
            var match = Regex.Match(doc.Raw(elements[i]), @"^\s*(T\w+)\s*\(");
            var type = match.Groups[1].Value;
            var constructors = doc.FindConstructors(type, elements[i]);
            if (!match.Success || constructors.Count != 1 || constructors[0].TypeTokenIndex != elements[i].StartTokenIndex || constructors[0].CloseTokenIndex != elements[i].EndTokenIndex)
            { notes.Add("未解释效果：" + doc.Raw(elements[i])); continue; }
            var c = constructors[0];
            string Value(string key) { var a = doc.FindDirectAssignments(c, key); return a.Count == 1 ? doc.Raw(a[0]) : ""; }
            var (field, label, mode, nonnegative) = type switch
            {
                "TUnitEffectIncreaseDamageTakenDescriptor" => ("BonusDamage", "压制承伤修饰（%）", "Pourcentage", false),
                "TUnitEffectIncreaseSpeedDescriptor" => ("BonusSpeedBaseInPercent", "速度修饰（%）", "Pourcentage", false),
                "TBonusWeaponAimtimeEffectDescriptor" => ("ModifierValue", "瞄准时间倍率", "Multiplicatif", true),
                "TUnitEffectIncreaseWeaponPrecisionArretDescriptor" => ("ModifierValue", "静止精度加值", "Additionnel", false),
                "TUnitEffectIncreaseWeaponPrecisionMouvementDescriptor" => ("ModifierValue", "移动精度加值", "Additionnel", false),
                "TUnitEffectAlterWeaponTempsEntreDeuxSalvesDescriptor" => ("ModifierValue", "两次齐射间隔倍率", "Multiplicatif", true),
                "TUnitEffectIncreaseWeaponDispersionMaxRangeDescriptor" => ("ModifierValue", "最大射程散布倍率", "Multiplicatif", true),
                "TUnitEffectHealOverTimeDescriptor" => ("HealUnitsPerSecond", "压制恢复每秒", "", true),
                "TUnitEffectBonusPrecisionWhenTargetedDescriptor" => ("BonusPrecisionWhenTargeted", "被瞄准精度加值", "Additionnel", false),
                _ => ("", "", "", false)
            };
            if (field.Length == 0) { notes.Add((type == "TUnitEffectRaiseTagDescriptor" ? "等级标签（保留）：" : "未解释效果：") + doc.Raw(elements[i])); continue; }
            var error = sharedError;
            if (mode.Length > 0 && Value("ModifierType") != "~/ModifierType_" + mode ||
                type is "TUnitEffectIncreaseDamageTakenDescriptor" or "TUnitEffectHealOverTimeDescriptor" && Value("DamageType") != "EDamageType/Suppress" ||
                type == "TUnitEffectHealOverTimeDescriptor" && Value("NbUpdatePerSecond") != "1") error = "当前效果语义不支持编辑";
            AddCell(source, doc, c, field, label, source.Name + "/" + i + "/" + field, nonnegative, cells, notes, error);
            if (error.Length > 0) notes.Add(source.Name + " · " + type + "\n" + doc.Raw(elements[i]));
        }
    }
    public static Dictionary<string, string> Values(DraftOperation operation) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(operation.TargetRaw) ?? throw new InvalidDataException("草稿为空");
    public static void Validate(ExperienceRoute route, ExperienceLevel level, IReadOnlyDictionary<string, string> values)
    {
        var editable = level.Cells.Where(c => c.Error.Length == 0).ToArray();
        if (route.Error.Length > 0 || level.Error.Length > 0 || editable.Length == 0 || values.Count != editable.Length || editable.Any(c => !values.ContainsKey(c.Key)))
            throw new InvalidDataException("经验路线结构已变化或无法编辑");
        foreach (var cell in editable)
            if (!decimal.TryParse(values[cell.Key], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || cell.Nonnegative && number < 0)
                throw new InvalidDataException(cell.Label + "：请输入有效数值" + (cell.Nonnegative ? "（不小于0）" : ""));
    }
    public DraftOperation Operation(ExperienceRoute route, ExperienceLevel level, IReadOnlyDictionary<string, string> values)
    {
        Validate(route, level, values);
        var key = level.Index.ToString(CultureInfo.InvariantCulture);
        var changed = level.Cells.Where(c => c.Error.Length == 0 && c.Raw != values[c.Key]).ToArray();
        return new(DraftOperation.CreateId(DraftTargetKind.ExperienceLevel, route.File, route.Name, key), "experience:" + route.Name,
            DraftTargetKind.ExperienceLevel, "rules", route.File, route.Name, "TExperienceLevelsPackDescriptor", key,
            "ExperienceLevelsDescriptors[" + key + "]", "ExperienceCells", string.Join("；", changed.Select(c => c.Field + "=" + c.Raw)),
            level.Baseline, string.Join("；", changed.Select(c => c.Field + "=" + values[c.Key])), JsonSerializer.Serialize(values),
            route.Alias + " · " + route.Name + " · 等级 " + key + " · " + string.Join("；", changed.Select(c => c.Label + "：" + c.Raw + " → " + values[c.Key])) +
            "；共享使用者 " + route.Users.Count, null, false, DateTimeOffset.UtcNow, EditScope: DraftEditScope.AllReferences);
    }
    private (ExperienceRoute Route, ExperienceLevel Level) Find(DraftOperation op)
    {
        var route = Routes.Single(r => r.Name == op.ObjectName && r.File == op.RelativeSourceFile);
        var level = route.Levels.Single(l => l.Index.ToString(CultureInfo.InvariantCulture) == op.FieldKey);
        return (route, level);
    }
    public ResolvedDraftOperation Resolve(DraftOperation op)
    {
        try
        {
            var (route, level) = Find(op);
            if (op.TargetKind != DraftTargetKind.ExperienceLevel || op.Module != "rules" || op.ObjectType != "TExperienceLevelsPackDescriptor" ||
                op.EditScope != DraftEditScope.AllReferences || op.BaselineRaw != level.Baseline ||
                op.Id != DraftOperation.CreateId(op.TargetKind, route.File, route.Name, op.FieldKey)) throw new InvalidDataException("路线、效果或共享影响基线已变化，请撤销此项后重新编辑");
            Validate(route, level, Values(op));
            return new(op, DraftResolutionStatus.Active, "");
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or JsonException or ArgumentException)
        { return new(op, DraftResolutionStatus.Conflict, ex.Message); }
    }
    public string Review(IEnumerable<DraftOperation> operations) => JsonSerializer.Serialize(operations
        .Where(o => o.TargetKind == DraftTargetKind.ExperienceLevel).OrderBy(o => o.Id, StringComparer.Ordinal)
        .Select(o => { var (route, level) = Find(o); return new { o.Id, level.Baseline, RouteError = route.Error, LevelError = level.Error }; }));

    public ExperienceWorkspace Plan(IReadOnlyList<DraftOperation> operations, List<PlannedFileChange> planned)
    {
        var active = operations.Where(o => o.TargetKind == DraftTargetKind.ExperienceLevel).ToArray();
        var replacements = new Dictionary<string, List<TextReplacement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in active)
        {
            var result = Resolve(op);
            if (result.Status != DraftResolutionStatus.Active) throw new TransactionValidationException(result.Reason);
            var (_, level) = Find(op); var values = Values(op);
            foreach (var cell in level.Cells.Where(c => c.Error.Length == 0 && c.Raw != values[c.Key]))
            {
                if (!replacements.TryGetValue(cell.File, out var list)) replacements[cell.File] = list = [];
                list.Add(new(cell.Offset, cell.Length, cell.Raw, values[cell.Key], op.Summary));
            }
        }
        foreach (var (file, edits) in replacements)
        {
            var snapshot = TextFileSnapshot.Load(Root, file, FormalTextFileKind.Ndf);
            if (snapshot.Text != _sources[file]) throw new TransactionValidationException("经验源文件在规划期间变化");
            var candidate = SemicolonCsvDocument.ApplyReplacements(snapshot.Text, edits);
            var prior = planned.SingleOrDefault(p => p.RelativePath.Equals(file, StringComparison.OrdinalIgnoreCase));
            if (prior is not null)
            {
                // Combine independent edits in the same file without reusing stale character offsets.
                candidate = MergeIndependent(snapshot.Text, Encoding.UTF8.GetString(prior.CandidateBytes), edits);
                planned.Remove(prior);
            }
            planned.Add(UnitApplyPlanner.ToWriteChange(snapshot, candidate, (prior?.Summaries ?? []).Concat(edits.Select(e => e.Description)).ToArray()));
        }
        var candidates = planned.Where(p => p.Kind == FormalTextFileKind.Ndf).ToDictionary(p => p.RelativePath, p => Encoding.UTF8.GetString(p.CandidateBytes), StringComparer.OrdinalIgnoreCase);
        var final = Load(Root, candidates);
        foreach (var op in active)
        {
            var (route, level) = final.Find(op); var values = Values(op);
            Validate(route, level, values);
            if (level.Cells.Any(c => c.Error.Length == 0 && c.Raw != values[c.Key])) throw new TransactionValidationException("经验字段候选值不一致");
        }
        return final;
    }
    private static string MergeIndependent(string original, string candidate, IReadOnlyList<TextReplacement> edits)
    {
        // Re-locate by the original top-level object, requiring its whole text to remain untouched.
        var oldObjects = new NdfTopLevelScanner().Scan(original, "original.ndf", "rules").Objects;
        var newObjects = new NdfTopLevelScanner().Scan(candidate, "candidate.ndf", "rules").Objects;
        var relocated = new List<TextReplacement>();
        foreach (var edit in edits)
        {
            var owner = oldObjects.Single(o => edit.Offset >= o.CharacterOffset && edit.Offset + edit.Length <= o.CharacterOffset + o.CharacterLength);
            var target = newObjects.Single(o => o.Name == owner.Name && o.TypeName == owner.TypeName);
            if (original.Substring(owner.CharacterOffset, owner.CharacterLength) != candidate.Substring(target.CharacterOffset, target.CharacterLength))
                throw new TransactionValidationException("同一经验对象存在重叠修改，请分开处理");
            relocated.Add(edit with { Offset = target.CharacterOffset + edit.Offset - owner.CharacterOffset });
        }
        return SemicolonCsvDocument.ApplyReplacements(candidate, relocated);
    }
}
