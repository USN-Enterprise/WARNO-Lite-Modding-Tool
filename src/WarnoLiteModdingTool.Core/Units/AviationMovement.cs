using System.Globalization;
using System.Text;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Units;

public static class AviationMovement
{
    public const string Speed = "movement.maxSpeed";
    public const string PlaneSpeed = "flight.speed";
    public const string HeliSpeed = "helicopter.speed";
    private const string Template = "AirplaneMovementDescriptor";
    private const string PlaneModule = "TAirplaneMovementModuleDescriptor";
    private const string HeliModule = "THelicopterMovementModuleDescriptor";
    public static IReadOnlyList<UnitFieldDefinition> Fields { get; } = [
        Plane("altitude", "飞行高度", "AltitudeGRU", "GRU", "常态飞行高度；攻击机动可改变高度，不代表独立入场高度"),
        Plane("minimumAltitude", "最低飞行高度", "AltitudeMinGRU", "GRU", "最低高度参数，不能高于常态飞行高度；不等于固定投弹高度"),
        Plane("speed", "固定翼运动速度", "SpeedInKmph", "km/h", "通过最大速度入口与通用速度一起修改"),
        Plane("turnRadius", "转弯半径", "AgilityRadiusGRU", "GRU", "机动半径参数，不是固定转弯用时"),
        Plane("pitch", "飞机俯仰角", "PitchAngleDegree", "°", "飞机运动俯仰角参数，不等于固定投弹俯冲角"),
        Plane("roll", "飞机滚转角", "RollAngleDegree", "°", "飞机运动滚转角参数"),
        Plane("rollSpeed", "滚转速度", "RollSpeedDegreePerSecond", "°/s", "飞机滚转角速度"),
        Plane("evacAngle", "撤离角度", "EvacAngleDegree", "°", "撤离运动角度；撤离高度在游戏规则中调整"),
        Heli("altitude", "直升机飞行高度", "FlyingAltitudeGRU", "GRU", "直升机常态飞行高度"),
        Heli("nearGroundAltitude", "近地飞行高度", "NearGroundFlyingAltitudeGRU", "GRU", "不能高于常态飞行高度；实际近地飞行还受地形规则影响"),
        Heli("speed", "直升机运动速度", "MaxSpeedInKmph", "km/h", "通过最大速度入口与通用速度一起修改"),
        Heli("upwardSpeed", "上升速度", "UpwardSpeedInKmph", "km/h", "直升机上升速度参数")
    ];
    private static UnitFieldDefinition Plane(string key, string label, string field, string unit, string hint) =>
        new("flight." + key, "固定翼飞行", label + "（" + unit + "）", hint, new(Template, field, AlternateModuleType: PlaneModule), UnitValueKind.Decimal, UnitEditorKind.Text, true, unit, Section: "机动与续航");
    private static UnitFieldDefinition Heli(string key, string label, string field, string unit, string hint) =>
        new("helicopter." + key, "直升机飞行", label + "（" + unit + "）", hint, new(HeliModule, field), UnitValueKind.Decimal, UnitEditorKind.Text, true, unit, Section: "机动与续航");
    public static bool IsField(string key) => key.StartsWith("flight.", StringComparison.Ordinal) || key.StartsWith("helicopter.", StringComparison.Ordinal);
    public static bool IsSpeed(string key) => key is Speed or PlaneSpeed or HeliSpeed;
    public static bool InternalSpeed(string key) => key is PlaneSpeed or HeliSpeed;
    public static bool Professional(string key) => key is "flight.minimumAltitude" or "flight.pitch" or "flight.roll" or "flight.rollSpeed" or "flight.evacAngle";
    public static bool Relevant(UnitRecord unit, string key) => !IsField(key) || unit.Fields.Any(f =>
        f.Definition.Key.StartsWith(key.StartsWith("flight.", StringComparison.Ordinal) ? "flight." : "helicopter.", StringComparison.Ordinal) && f.Availability != UnitFieldAvailability.Missing);

    public static IReadOnlyList<UnitFieldValue> SpeedFields(UnitRecord unit)
    {
        var result = new List<UnitFieldValue>();
        if (unit.Field(Speed) is { } generic) result.Add(generic);
        foreach (var key in new[] { PlaneSpeed, HeliSpeed })
            if (Relevant(unit, key) && unit.Field(key) is { } specific) result.Add(specific);
        return result;
    }
    public static string SpeedHint(UnitRecord unit) => SpeedFields(unit).Count <= 1 ? "通用移动模块速度" :
        "修改时同步通用与航空运动速度；恢复基线时保留原有速度差异";

    // A speed edit is persisted as explicit paired drafts, with independent baselines.
    // Returning to the generic baseline restores each original value, even if they differed.
    public static (IReadOnlyList<DraftOperation> Upserts, IReadOnlyList<string> Removals) SpeedDrafts(UnitRecord unit, string input)
    {
        var fields = SpeedFields(unit);
        if (fields.Count == 0 || fields.Any(f => !f.CanEdit || f.Location is null)) throw new InvalidOperationException("航空速度关联字段缺失、多义或不是直接数值，无法安全同步");
        if (!UnitValueConverter.TryFormatTarget(fields[0], input, out var normalized, out _, out var error)) throw new InvalidOperationException(error);
        if (fields.Count > 1 && Number(normalized) <= 0) throw new InvalidOperationException("航空速度必须大于零");
        var reset = normalized == fields[0].DisplayValue;
        var adds = new List<DraftOperation>(); var removes = new List<string>();
        foreach (var field in fields)
        {
            if (!UnitValueConverter.TryFormatTarget(field, reset ? field.DisplayValue : normalized, out var value, out var raw, out error)) throw new InvalidOperationException(error);
            var id = DraftOperation.CreateId(DraftTargetKind.NdfField, field.Location!.RelativeSourceFile, unit.Name, field.Definition.Key);
            if (value == field.DisplayValue) { removes.Add(id); continue; }
            adds.Add(new(id, "aviation-speed:" + unit.Name, DraftTargetKind.NdfField, "units", field.Location.RelativeSourceFile, unit.Name, unit.Source.TypeName,
                field.Definition.Key, field.Location.FieldPath, field.Definition.ValueKind.ToString(), field.DisplayValue, field.RawValue, value, raw,
                $"{unit.DisplayName} · {field.Definition.Label}：{field.DisplayValue} → {value}", null, false, DateTimeOffset.UtcNow));
        }
        return (adds, removes);
    }

    public static bool ValidTarget(string key, double number, out string error)
    {
        error = string.Empty;
        if (key is PlaneSpeed or HeliSpeed or "flight.turnRadius" or "flight.rollSpeed" or "helicopter.upwardSpeed" && number <= 0)
            error = "航空速度、转弯半径和角速度必须大于零";
        return error.Length == 0;
    }

    public static void ValidateRestored(string root, IReadOnlyList<Changes.RestoreFile> restored)
    {
        var units = new List<UnitRecord>(); var edits = new List<DraftOperation>(); var candidates = new List<PlannedFileChange>();
        foreach (var file in restored.Where(f => f.After is not null && f.Path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase)))
        {
            var text = Changes.ChangeMerge.DecodeNdf(file.After!);
            var before = file.Before is null ? "" : Changes.ChangeMerge.DecodeNdf(file.Before);
            if (text == before || !text.Contains("Movement", StringComparison.Ordinal)) continue;
            var full = Path.Combine(root, file.Path);
            var oldObjects = new NdfTopLevelScanner().Scan(before, full, "units", root).Objects.Where(o => o.TypeName == "TEntityDescriptor").ToDictionary(o => o.Name);
            var changed = new NdfTopLevelScanner().Scan(text, full, "units", root).Objects.Where(o => o.TypeName == "TEntityDescriptor" &&
                (!oldObjects.TryGetValue(o.Name, out var oldObject) || !text.AsSpan(o.CharacterOffset, o.CharacterLength).SequenceEqual(before.AsSpan(oldObject.CharacterOffset, oldObject.CharacterLength)))).ToArray();
            IReadOnlyList<UnitRecord> ReadUnits(string source, IReadOnlyList<NdfObjectInfo> objects) => new UnitCatalogBuilder().Build(objects,
                new Dictionary<string, string> { [full] = source }, applyChoices: false);
            var old = ReadUnits(before, changed.Where(o => oldObjects.ContainsKey(o.Name)).Select(o => oldObjects[o.Name]).ToArray()).ToDictionary(u => u.Name);
            var current = ReadUnits(text, changed);
            foreach (var unit in current)
            {
                foreach (var field in unit.Fields.Where(f => (IsField(f.Definition.Key) || f.Definition.Key == Speed) && f.Availability != UnitFieldAvailability.Missing))
                {
                    if (old.GetValueOrDefault(unit.Name)?.Field(field.Definition.Key)?.RawValue == field.RawValue) continue;
                    if (!field.CanEdit) throw new TransactionValidationException("还原后的航空字段不唯一或不是直接数值：" + unit.Name);
                    edits.Add(new(field.Definition.Key, null, DraftTargetKind.NdfField, "units", file.Path, unit.Name, unit.Source.TypeName,
                        field.Definition.Key, "", "", "", "", field.DisplayValue, field.RawValue, "", null, false, DateTimeOffset.UtcNow));
                }
            }
            units.AddRange(current);
            candidates.Add(new(file.Path, full, FormalTextFileKind.Ndf, PlannedFileAction.Write, file.Before is not null, file.Before ?? [], file.After!, null, []));
        }
        // All selected NDF candidates are supplied, including a changed shared ceiling.
        foreach (var file in restored.Where(f => f.After is not null && f.Path.EndsWith(".ndf", StringComparison.OrdinalIgnoreCase) && candidates.All(c => c.RelativePath != f.Path)))
            candidates.Add(new(file.Path, Path.Combine(root, file.Path), FormalTextFileKind.Ndf, PlannedFileAction.Write, file.Before is not null, file.Before ?? [], file.After!, null, []));
        ValidateFinal(root, units, edits, candidates);
    }
    private static double Number(string raw) => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n)
        ? n : throw new TransactionValidationException("航空关联值不是直接有限数值，不能确认组合结果");

    public static void ValidateUnit(UnitRecord unit, IReadOnlyList<DraftOperation> operations)
    {
        var edits = operations.Where(o => o.TargetKind == DraftTargetKind.NdfField && o.ObjectName == unit.Name).ToArray();
        string Value(string key) => edits.LastOrDefault(o => o.FieldKey == key)?.TargetValue ?? unit.Field(key)?.DisplayValue ?? "";
        foreach (var pair in new[] { ("flight.minimumAltitude", "flight.altitude"), ("helicopter.nearGroundAltitude", "helicopter.altitude") })
            if (edits.Any(o => o.FieldKey == pair.Item1 || o.FieldKey == pair.Item2) && Number(Value(pair.Item1)) > Number(Value(pair.Item2)))
                throw new TransactionValidationException("最低/近地飞行高度不能高于常态飞行高度：" + unit.DisplayName);
        if (edits.Any(o => IsSpeed(o.FieldKey)) && SpeedFields(unit).Count > 1)
        {
            var fields = SpeedFields(unit);
            if (fields.Any(f => !f.CanEdit) || fields.Select(f => Number(Value(f.Definition.Key))).Distinct().Count() != 1 || Number(Value(Speed)) <= 0)
                throw new TransactionValidationException("航空速度必须与通用速度同时修改并保持一致：" + unit.DisplayName);
        }
    }

    // Validate final candidates, including creation drafts; upper limits are read from
    // the selected Mod, never supplied from a development sample or a hard-coded value.
    public static Dictionary<string, byte[]> ValidateFinal(string root, IReadOnlyList<UnitRecord> units, IReadOnlyList<DraftOperation> operations, IReadOnlyList<PlannedFileChange> files)
    {
        var dependencies = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in units.Where(u => operations.Any(o => o.ObjectName == u.Name && (IsField(o.FieldKey) || IsSpeed(o.FieldKey))))) ValidateUnit(unit, operations);
        var heightUnits = operations.Where(o => o.TargetKind == DraftTargetKind.NdfField && o.FieldKey is "flight.altitude" or "flight.minimumAltitude")
            .Select(o => o.ObjectName).ToHashSet(StringComparer.Ordinal);
        foreach (var op in operations.Where(o => o.TargetKind == DraftTargetKind.UnitCreate))
            if (UnitCreation.Read(op).Fields.Keys.Any(k => k is "flight.altitude" or "flight.minimumAltitude")) heightUnits.Add(op.ObjectName);
        foreach (var rename in operations.Where(o => o.TargetKind == DraftTargetKind.UnitRename))
            if (heightUnits.Remove(rename.ObjectName)) heightUnits.Add(UnitIdentityEditing.Read(rename).NewName);
        if (heightUnits.Count == 0) return dependencies;
        var validated = new HashSet<string>(StringComparer.Ordinal);
        double? templateCeiling = null;
        foreach (var file in files.Where(f => f.Kind == FormalTextFileKind.Ndf && f.Action != PlannedFileAction.Delete))
        {
            var text = Encoding.UTF8.GetString(file.CandidateBytes);
            foreach (var obj in new NdfTopLevelScanner().Scan(text, file.RelativePath, "units").Objects.Where(o => heightUnits.Contains(o.Name)))
            {
                var doc = new NdfSyntaxDocument(text, obj.CharacterOffset, obj.CharacterLength);
                var direct = doc.FindConstructors(PlaneModule);
                var module = direct.Count == 1 ? direct[0] : doc.FindConstructors(Template).Single();
                double Field(string name) { var matches = doc.FindDirectAssignments(module, name); return matches.Count == 1 ? Number(doc.Raw(matches[0])) : throw new TransactionValidationException("飞机高度字段缺失或多义：" + name); }
                var ceiling = direct.Count == 1 ? Field("AltitudeMaxGRU") : templateCeiling ??= ReadTemplateCeiling(root, files, dependencies);
                if (Field("AltitudeMinGRU") > Field("AltitudeGRU") || Field("AltitudeGRU") > ceiling)
                    throw new TransactionValidationException("飞机高度须满足最低高度 ≤ 常态高度 ≤ 当前Mod最大高度：" + obj.Name);
                validated.Add(obj.Name);
            }
        }
        if (!heightUnits.SetEquals(validated)) throw new TransactionValidationException("最终飞机高度对象无法完整定位，修改未应用");
        return dependencies;
    }
    private static double ReadTemplateCeiling(string root, IReadOnlyList<PlannedFileChange> files, Dictionary<string, byte[]> dependencies)
    {
        string Read(string path)
        {
            var snapshot = TextFileSnapshot.Load(root, path, FormalTextFileKind.Ndf);
            dependencies[path] = snapshot.OriginalBytes;
            var candidate = files.SingleOrDefault(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            return candidate is null ? snapshot.Text : Encoding.UTF8.GetString(candidate.CandidateBytes);
        }
        const string templatePath = "GameData/Gameplay/Unit/Tactic/ModulesStandard.ndf";
        var doc = new NdfSyntaxDocument(Read(templatePath));
        var tokens = doc.Tokens;
        var declarations = Enumerable.Range(0, Math.Max(0, tokens.Count - 2)).Where(i => tokens[i].Text == "template" && tokens[i + 1].Text == Template && tokens[i + 2].Text == "[").ToArray();
        if (declarations.Length != 1) throw new TransactionValidationException("无法确认当前Mod的飞机运动模板；高度修改未应用");
        var start = declarations[0]; var end = start + 3;
        while (end < tokens.Count && tokens[end].Text != "]") end++;
        if (end + 2 >= tokens.Count || tokens[end + 1].Text != "is" || tokens[end + 2].Text != PlaneModule) throw new TransactionValidationException("飞机运动模板类型不受支持");
        var module = doc.FindConstructors(PlaneModule).SingleOrDefault(c => c.TypeTokenIndex == end + 2) ?? throw new TransactionValidationException("飞机运动模板不唯一");
        foreach (var key in new[] { "AltitudeGRU", "AltitudeMinGRU" })
        {
            var values = doc.FindDirectAssignments(module, key);
            if (values.Count != 1 || string.Concat(doc.Raw(values[0]).Where(c => !char.IsWhiteSpace(c))) != "<" + key + ">") throw new TransactionValidationException("飞机高度模板映射不受支持");
        }
        var max = doc.FindDirectAssignments(module, "AltitudeMaxGRU");
        if (max.Count != 1) throw new TransactionValidationException("飞机最大高度缺失或多义");
        var raw = doc.Raw(max[0]);
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var literal) && double.IsFinite(literal)) return literal;
        // The supported standard template uses this exact relative constant binding.
        if (raw != "~/MaxAltitudeGRU") throw new TransactionValidationException("飞机最大高度引用不受支持，不能确认上限");
        var constants = Read("GameData/Gameplay/Unit/AirplaneConstantes.ndf");
        var definition = new Rules.RuleDefinition(0, "飞机最大高度", "", "", "MaxAltitudeGRU", null);
        var group = Rules.RuleWorkspace.Read(definition, constants);
        if (!group.CanEdit || group.Cells.Count != 1) throw new TransactionValidationException("飞机最大高度常量缺失、多义或不是直接数值");
        return Number(group.Cells[0].Raw);
    }
}
