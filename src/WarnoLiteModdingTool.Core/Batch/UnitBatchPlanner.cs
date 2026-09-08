using System.Globalization;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Batch;

public enum UnitBatchOperation
{
    Set,
    Multiply,
    Add,
    IncreasePercent,
    DecreasePercent,
    Subtract
}

public enum UnitBatchRounding
{
    None,
    Nearest,
    Floor,
    Ceiling
}

public sealed record UnitBatchRequest(
    string BatchId,
    IReadOnlyList<UnitRecord> Targets,
    string FieldKey,
    UnitBatchOperation Operation,
    string Operand,
    UnitBatchRounding Rounding,
    string? Minimum,
    string? Maximum,
    IReadOnlyList<DraftOperation> ExistingDrafts);

public sealed record UnitBatchSample(
    string UnitName,
    string DisplayName,
    string CurrentValue,
    string TargetValue);

public sealed record UnitBatchImpact(
    int DivisionCount,
    int WeaponCount,
    int AmmoCount,
    IReadOnlyList<string> Divisions,
    IReadOnlyList<string> Weapons,
    IReadOnlyList<string> Ammunition);

public sealed record UnitBatchPreview(
    int TargetCount,
    int CompatibleCount,
    int ChangedCount,
    int UnchangedCount,
    IReadOnlyList<UnitBatchSample> Samples,
    UnitBatchImpact Impact,
    IReadOnlyList<DraftOperation> Upserts,
    IReadOnlyList<string> RemoveOperationIds,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool CanAddToDrafts => Errors.Count == 0 && ChangedCount > 0;
}

public static class UnitBatchPlanner
{
    private const int SampleLimit = 5;

    public static UnitBatchPreview Preview(UnitBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var targets = request.Targets
            .GroupBy(unit => unit.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var errors = new List<string>();
        var warnings = new List<string>();
        var samples = new List<UnitBatchSample>();
        var upserts = new List<DraftOperation>();
        var removals = new List<string>();
        var changedUnits = new List<UnitRecord>();
        var compatibleCount = 0;
        var unchangedCount = 0;

        if (targets.Length == 0)
        {
            errors.Add("当前范围没有 Unit。请勾选 Unit 或调整筛选条件。");
        }

        if (string.IsNullOrWhiteSpace(request.BatchId))
        {
            errors.Add("批次编号不能为空。");
        }

        var definition = UnitFieldDefinitions.All.FirstOrDefault(item => item.Key == request.FieldKey);
        if (definition is null)
        {
            errors.Add("请选择已支持的 Unit 字段。");
            return Empty(targets.Length, errors);
        }

        var isFormula = request.Operation != UnitBatchOperation.Set;
        if (isFormula && !IsNumeric(definition.ValueKind))
        {
            errors.Add($"“{definition.Label}”不是数值字段，只能使用“设为固定值”。");
        }

        decimal? minimum = null;
        decimal? maximum = null;
        if (isFormula &&
            (!TryReadOptionalNumber(request.Minimum, "最小值", out minimum, errors) |
             !TryReadOptionalNumber(request.Maximum, "最大值", out maximum, errors)))
        {
            return Empty(targets.Length, errors);
        }

        if (minimum is not null && maximum is not null && minimum > maximum)
        {
            errors.Add("最小值不能大于最大值。");
        }

        decimal operandNumber = 0;
        if (isFormula && !TryReadNumber(request.Operand, "公式参数", out operandNumber, errors))
        {
            return Empty(targets.Length, errors);
        }

        if (errors.Count > 0)
        {
            return Empty(targets.Length, errors);
        }

        var existingByUnit = request.ExistingDrafts
            .Where(item => item.TargetKind == DraftTargetKind.NdfField &&
                           item.Module == "units" &&
                           item.FieldKey == definition.Key)
            .GroupBy(item => item.ObjectName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.UpdatedUtc).First(), StringComparer.Ordinal);
        var skippedReasons = new Dictionary<string, int>(StringComparer.CurrentCulture);
        var targetErrors = new Dictionary<string, int>(StringComparer.CurrentCulture);

        foreach (var unit in targets)
        {
            var field = unit.Field(definition.Key);
            if (field is null || !field.CanEdit || field.Location is null)
            {
                unchangedCount++;
                var reason = field?.Reason;
                if (string.IsNullOrWhiteSpace(reason))
                {
                    reason = "字段不存在或不可编辑";
                }

                skippedReasons[reason] = skippedReasons.GetValueOrDefault(reason) + 1;
                continue;
            }

            compatibleCount++;
            existingByUnit.TryGetValue(unit.Name, out var existing);
            var currentValue = existing?.TargetValue ?? field.DisplayValue;
            string candidateInput;
            if (request.Operation == UnitBatchOperation.Set)
            {
                candidateInput = request.Operand;
            }
            else
            {
                if (!decimal.TryParse(currentValue.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var current))
                {
                    targetErrors["当前显示值不是可计算数字"] = targetErrors.GetValueOrDefault("当前显示值不是可计算数字") + 1;
                    continue;
                }

                try
                {
                    var result = request.Operation switch
                    {
                        UnitBatchOperation.Multiply => current * operandNumber,
                        UnitBatchOperation.Add => current + operandNumber,
                        UnitBatchOperation.Subtract => current - operandNumber,
                        UnitBatchOperation.IncreasePercent => current * (1 + operandNumber / 100),
                        UnitBatchOperation.DecreasePercent => current * (1 - operandNumber / 100),
                        _ => current
                    };
                    if (minimum is not null)
                    {
                        result = Math.Max(result, minimum.Value);
                    }

                    if (maximum is not null)
                    {
                        result = Math.Min(result, maximum.Value);
                    }

                    result = request.Rounding switch
                    {
                        UnitBatchRounding.Nearest => decimal.Round(result, 0, MidpointRounding.AwayFromZero),
                        UnitBatchRounding.Floor => decimal.Floor(result),
                        UnitBatchRounding.Ceiling => decimal.Ceiling(result),
                        _ => result
                    };
                    candidateInput = result.ToString("0.##########", CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    targetErrors["公式结果超出可表示范围"] = targetErrors.GetValueOrDefault("公式结果超出可表示范围") + 1;
                    continue;
                }
            }

            if (!UnitValueConverter.TryFormatTarget(field, candidateInput, out var normalized, out var targetRaw, out var error))
            {
                targetErrors[error] = targetErrors.GetValueOrDefault(error) + 1;
                continue;
            }

            if (definition.Key.StartsWith("armor.", StringComparison.Ordinal) &&
                !definition.Key.EndsWith(".family", StringComparison.Ordinal))
            {
                var familyKey = definition.Key + ".family";
                var familyRaw = request.ExistingDrafts.FirstOrDefault(item => item.ObjectName == unit.Name && item.FieldKey == familyKey)?.TargetRaw
                                ?? unit.Field(familyKey)?.RawValue ?? string.Empty;
                if (string.Equals(WarnoLiteModdingTool.Core.Ndf.NdfSyntaxDocument.Leaf(familyRaw), "ResistanceFamily_infanterie", StringComparison.Ordinal) && normalized != "1")
                {
                    targetErrors["步兵护甲族的装甲索引固定为 1"] = targetErrors.GetValueOrDefault("步兵护甲族的装甲索引固定为 1") + 1;
                    continue;
                }
            }

            if (string.Equals(normalized, currentValue, StringComparison.Ordinal))
            {
                unchangedCount++;
                continue;
            }

            changedUnits.Add(unit);
            if (samples.Count < SampleLimit)
            {
                samples.Add(new UnitBatchSample(unit.Name, unit.DisplayName, currentValue, normalized));
            }

            if (string.Equals(normalized, field.DisplayValue, StringComparison.Ordinal))
            {
                if (existing is not null)
                {
                    removals.Add(existing.Id);
                }
                else
                {
                    unchangedCount++;
                    changedUnits.RemoveAt(changedUnits.Count - 1);
                    if (samples.Count > 0 && samples[^1].UnitName == unit.Name)
                    {
                        samples.RemoveAt(samples.Count - 1);
                    }
                }

                continue;
            }

            upserts.Add(new DraftOperation(
                DraftOperation.CreateId(DraftTargetKind.NdfField, field.Location.RelativeSourceFile, unit.Name, definition.Key),
                $"batch:{request.BatchId}",
                DraftTargetKind.NdfField,
                "units",
                field.Location.RelativeSourceFile,
                unit.Name,
                unit.Source.TypeName,
                definition.Key,
                field.Location.FieldPath,
                definition.ValueKind.ToString(),
                field.DisplayValue,
                field.RawValue,
                normalized,
                targetRaw,
                $"{unit.DisplayName} · {definition.Label}：{field.DisplayValue} → {normalized}",
                null,
                false,
                DateTimeOffset.UtcNow));

            if (definition.Key.EndsWith(".family", StringComparison.Ordinal) &&
                string.Equals(WarnoLiteModdingTool.Core.Ndf.NdfSyntaxDocument.Leaf(targetRaw), "ResistanceFamily_infanterie", StringComparison.Ordinal))
            {
                var indexKey = definition.Key[..^".family".Length];
                var indexField = unit.Field(indexKey);
                if (indexField?.CanEdit != true || indexField.Location is null)
                {
                    targetErrors["步兵护甲族无法联动唯一装甲索引"] = targetErrors.GetValueOrDefault("步兵护甲族无法联动唯一装甲索引") + 1;
                    continue;
                }

                var existingIndex = request.ExistingDrafts.FirstOrDefault(item => item.ObjectName == unit.Name && item.FieldKey == indexKey);
                var currentIndex = existingIndex?.TargetValue ?? indexField.DisplayValue;
                if (currentIndex != "1")
                {
                    if (indexField.DisplayValue == "1")
                    {
                        if (existingIndex is not null)
                        {
                            removals.Add(existingIndex.Id);
                        }
                    }
                    else
                    {
                        upserts.Add(new DraftOperation(
                            DraftOperation.CreateId(DraftTargetKind.NdfField, indexField.Location.RelativeSourceFile, unit.Name, indexKey),
                            $"batch:{request.BatchId}", DraftTargetKind.NdfField, "units", indexField.Location.RelativeSourceFile,
                            unit.Name, unit.Source.TypeName, indexKey, indexField.Location.FieldPath,
                            indexField.Definition.ValueKind.ToString(), indexField.DisplayValue, indexField.RawValue,
                            "1", "1", $"{unit.DisplayName} · {indexField.Definition.Label}：{indexField.DisplayValue} → 1（步兵护甲族联动）",
                            null, false, DateTimeOffset.UtcNow));
                    }
                }
            }
        }

        foreach (var (reason, count) in skippedReasons.OrderBy(item => item.Key, StringComparer.CurrentCulture))
        {
            warnings.Add($"{count} 个 Unit 未命中：{reason}");
        }

        foreach (var (reason, count) in targetErrors.OrderBy(item => item.Key, StringComparer.CurrentCulture))
        {
            errors.Add($"{count} 个 Unit 无法处理：{reason}");
        }

        if (compatibleCount == 0 && targets.Length > 0)
        {
            errors.Add($"当前范围没有可编辑的“{definition.Label}”字段。");
        }

        if (errors.Count > 0)
        {
            upserts.Clear();
            removals.Clear();
            changedUnits.Clear();
            samples.Clear();
            unchangedCount = targets.Length;
        }

        var impact = BuildImpact(changedUnits);
        return new UnitBatchPreview(
            targets.Length,
            compatibleCount,
            changedUnits.Count,
            unchangedCount,
            samples,
            impact,
            upserts,
            removals,
            warnings,
            errors);
    }

    private static bool IsNumeric(UnitValueKind kind) =>
        kind is UnitValueKind.Integer or UnitValueKind.Decimal or UnitValueKind.EcmPercent;

    private static bool TryReadNumber(string value, string label, out decimal number, ICollection<string> errors)
    {
        if (!decimal.TryParse(value.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            errors.Add($"{label}请输入有效数字，使用小数点而不是逗号。");
            return false;
        }

        return true;
    }

    private static bool TryReadOptionalNumber(string? value, string label, out decimal? number, ICollection<string> errors)
    {
        number = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!TryReadNumber(value, label, out var parsed, errors))
        {
            return false;
        }

        number = parsed;
        return true;
    }

    private static UnitBatchImpact BuildImpact(IEnumerable<UnitRecord> units)
    {
        var materialized = units.ToArray();
        var divisions = materialized.SelectMany(unit => unit.Divisions).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var weapons = materialized.SelectMany(unit => unit.Weapons).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var ammunition = materialized.SelectMany(unit => unit.Ammunition).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new UnitBatchImpact(divisions.Length, weapons.Length, ammunition.Length, divisions, weapons, ammunition);
    }

    private static UnitBatchPreview Empty(int targetCount, IReadOnlyList<string> errors) =>
        new(targetCount, 0, 0, targetCount, [], BuildImpact([]), [], [], [], errors);
}
