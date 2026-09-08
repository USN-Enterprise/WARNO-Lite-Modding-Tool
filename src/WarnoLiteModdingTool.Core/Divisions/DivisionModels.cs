using System.Text.Json;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Divisions;

public sealed record DivisionTextLocation(
    string RelativeSourceFile,
    int CharacterOffset,
    int CharacterLength,
    string RawValue,
    string FieldPath);

public sealed record DivisionUnitRuleState(
    string Unit,
    bool AvailableWithoutTransport,
    IReadOnlyList<string> AvailableTransports,
    int MaxPackNumber,
    int NumberOfUnitInPack,
    IReadOnlyList<double> XpMultipliers);

public sealed record DivisionDeckPackState(
    string? OriginalPackName,
    string Unit,
    string? Transport,
    int Xp,
    int Number);

public sealed record DivisionCostCurveState(
    string Category,
    IReadOnlyList<int> Costs);

public sealed record DivisionEditState(
    int MaxActivationPoints,
    double InterfaceOrder,
    string Coalition,
    IReadOnlyList<string> Tags,
    string CountryId,
    string TypeToken,
    IReadOnlyList<string> StandoutUnits,
    IReadOnlyList<DivisionUnitRuleState> UnitRules,
    IReadOnlyList<DivisionDeckPackState> DefaultDeck,
    IReadOnlyList<DivisionCostCurveState> CostCurves);

public sealed record DeckPackRecord(
    NdfObjectInfo Source,
    int Xp,
    string Unit,
    string? Transport,
    int Number)
{
    public string Name => Source.Name;

    public bool Matches(DivisionDeckPackState state) =>
        Xp == state.Xp &&
        Number == state.Number &&
        string.Equals(Unit, state.Unit, StringComparison.Ordinal) &&
        string.Equals(Transport ?? string.Empty, state.Transport ?? string.Empty, StringComparison.Ordinal);
}

public sealed class DivisionRecord
{
    public DivisionRecord(
        NdfObjectInfo source,
        string displayName,
        string divisionRuleName,
        string costMatrixName,
        string defaultDeckName,
        DivisionEditState baseline,
        IReadOnlyDictionary<string, DivisionTextLocation> divisionFields,
        DivisionTextLocation unitRuleList,
        DivisionTextLocation deckPackList,
        DivisionTextLocation costMatrix,
        bool canEdit,
        string editReason)
    {
        Source = source;
        DisplayName = displayName;
        DivisionRuleName = divisionRuleName;
        CostMatrixName = costMatrixName;
        DefaultDeckName = defaultDeckName;
        Baseline = baseline;
        DivisionFields = divisionFields;
        UnitRuleList = unitRuleList;
        DeckPackList = deckPackList;
        CostMatrix = costMatrix;
        CanEdit = canEdit;
        EditReason = editReason;
    }

    public NdfObjectInfo Source { get; }
    public string Name => Source.Name;
    public string DisplayName { get; }
    public string DivisionRuleName { get; }
    public string CostMatrixName { get; }
    public string DefaultDeckName { get; }
    public DivisionEditState Baseline { get; }
    public IReadOnlyDictionary<string, DivisionTextLocation> DivisionFields { get; }
    public DivisionTextLocation UnitRuleList { get; }
    public DivisionTextLocation DeckPackList { get; }
    public DivisionTextLocation CostMatrix { get; }
    public bool CanEdit { get; }
    public string EditReason { get; }
}

public sealed record DivisionWorkspaceData(
    string ProjectRoot,
    IReadOnlyList<DivisionRecord> Divisions,
    IReadOnlyDictionary<string, DeckPackRecord> DeckPacks,
    UnitWorkspaceData Units,
    bool HasCompleteFileSet,
    IReadOnlyList<string> Diagnostics)
{
    public int SkippedNonTacticalDescriptorCount { get; init; }

    public DivisionRecord? Division(string name) =>
        Divisions.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
}

public static class DivisionDraftCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(DivisionEditState state) => JsonSerializer.Serialize(state, Options);

    public static bool TryDeserialize(string value, out DivisionEditState state, out string error)
    {
        try
        {
            state = JsonSerializer.Deserialize<DivisionEditState>(value, Options)
                ?? throw new JsonException("草稿内容为空");
            error = string.Empty;
            return true;
        }
        catch (JsonException exception)
        {
            state = Empty;
            error = exception.Message;
            return false;
        }
    }

    public static DivisionEditState Empty { get; } = new(
        0, 0, string.Empty, [], string.Empty, string.Empty, [], [], [], []);
}

public static class DivisionStateValidator
{
    public static IReadOnlyList<string> Validate(
        DivisionWorkspaceData workspace,
        DivisionRecord division,
        DivisionEditState state)
    {
        var errors = new List<string>();
        var unitNames = workspace.Units.Units.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var unitsByName = workspace.Units.Units.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var baselineRules = division.Baseline.UnitRules.ToDictionary(item => item.Unit, StringComparer.Ordinal);
        if (state.MaxActivationPoints < 0)
        {
            errors.Add("总激活点不能小于 0");
        }

        if (!double.IsFinite(state.InterfaceOrder) || state.InterfaceOrder < 0)
        {
            errors.Add("展示顺序必须是不小于 0 的数字");
        }

        ValidateAtom(state.Coalition, "阵营", errors);
        ValidateAtom(state.CountryId, "国家", errors);
        ValidateAtom(state.TypeToken, "师类型", errors);
        if (state.Tags.Count == 0 || state.Tags.Any(string.IsNullOrWhiteSpace) ||
            state.Tags.Distinct(StringComparer.Ordinal).Count() != state.Tags.Count)
        {
            errors.Add("师标签不能为空或重复");
        }

        if (state.StandoutUnits.Any(unit => !unitNames.Contains(unit)) ||
            state.StandoutUnits.Distinct(StringComparer.Ordinal).Count() != state.StandoutUnits.Count)
        {
            errors.Add("特色单位必须存在且不能重复");
        }

        var rulesByUnit = new Dictionary<string, DivisionUnitRuleState>(StringComparer.Ordinal);
        foreach (var rule in state.UnitRules)
        {
            if (!unitNames.Contains(rule.Unit))
            {
                errors.Add($"单位池中的 Unit 不存在：{rule.Unit}");
            }

            if (!rulesByUnit.TryAdd(rule.Unit, rule))
            {
                errors.Add($"同一师内 UnitRule 重复：{rule.Unit}");
            }

            if (rule.MaxPackNumber < 0 || rule.NumberOfUnitInPack <= 0)
            {
                errors.Add($"{rule.Unit} 的最大卡数不能为负，单卡数量必须大于 0");
            }

            if (rule.XpMultipliers.Count == 0 || rule.XpMultipliers.Any(value => !double.IsFinite(value) || value < 0))
            {
                errors.Add($"{rule.Unit} 的老练度倍率必须是非负有限数字");
            }

            if (rule.AvailableTransports.Any(unit => !unitNames.Contains(unit)) ||
                rule.AvailableTransports.Distinct(StringComparer.Ordinal).Count() != rule.AvailableTransports.Count)
            {
                errors.Add($"{rule.Unit} 的运输列表包含不存在或重复的 Unit");
            }

            var baselineTransports = baselineRules.GetValueOrDefault(rule.Unit)?.AvailableTransports ?? [];
            foreach (var transport in rule.AvailableTransports.Where(item => !baselineTransports.Contains(item, StringComparer.Ordinal)))
            {
                if (!unitsByName.TryGetValue(transport, out var transportUnit) || !transportUnit.HasUniqueTransporterModule)
                {
                    errors.Add($"{rule.Unit} 新增的运输 {transport} 不具备唯一可识别的运输模块");
                }
            }
        }

        var packCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var categoryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pack in state.DefaultDeck)
        {
            if (!rulesByUnit.TryGetValue(pack.Unit, out var rule))
            {
                errors.Add($"默认卡组 Unit 不在师单位池：{pack.Unit}");
                continue;
            }

            if (pack.Number <= 0)
            {
                errors.Add($"{pack.Unit} 的 Pack 数量必须大于 0");
            }

            if (pack.Xp < 0 || pack.Xp >= rule.XpMultipliers.Count ||
                (pack.Xp >= 0 && pack.Xp < rule.XpMultipliers.Count && rule.XpMultipliers[pack.Xp] <= 0))
            {
                errors.Add($"{pack.Unit} 的老练度 {pack.Xp} 不受当前 UnitRule 支持");
            }

            if (string.IsNullOrWhiteSpace(pack.Transport))
            {
                if (!rule.AvailableWithoutTransport)
                {
                    errors.Add($"{pack.Unit} 不能无运输部署");
                }
            }
            else if (!rule.AvailableTransports.Contains(pack.Transport, StringComparer.Ordinal))
            {
                errors.Add($"{pack.Unit} 不允许使用运输 {pack.Transport}");
            }

            packCounts[pack.Unit] = packCounts.GetValueOrDefault(pack.Unit) + Math.Max(0, pack.Number);
            var unit = workspace.Units.Units.FirstOrDefault(item => item.Name == pack.Unit);
            var category = unit?.Factory;
            if (string.IsNullOrWhiteSpace(category))
            {
                errors.Add($"无法识别 {pack.Unit} 的生产栏位");
            }
            else
            {
                categoryCounts[category] = categoryCounts.GetValueOrDefault(category) + Math.Max(0, pack.Number);
            }
        }

        foreach (var pair in packCounts)
        {
            if (rulesByUnit.TryGetValue(pair.Key, out var rule) && pair.Value > rule.MaxPackNumber)
            {
                errors.Add($"{pair.Key} 默认卡组数量 {pair.Value} 超过最大卡数 {rule.MaxPackNumber}");
            }
        }

        var curves = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        foreach (var curve in state.CostCurves)
        {
            if (!curves.TryAdd(NdfSyntaxDocument.Leaf(curve.Category), curve.Costs))
            {
                errors.Add($"激活费用类别重复：{curve.Category}");
            }

            if (curve.Costs.Any(cost => cost < 0))
            {
                errors.Add($"{curve.Category} 的激活费用不能为负");
            }
        }

        var activationPoints = 0;
        foreach (var pair in categoryCounts)
        {
            var category = NdfSyntaxDocument.Leaf(pair.Key);
            if (!curves.TryGetValue(category, out var costs) || costs.Count < pair.Value)
            {
                errors.Add($"{category} 需要 {pair.Value} 个卡位，但费用曲线容量不足");
                continue;
            }

            activationPoints += costs.Take(pair.Value).Sum();
        }

        if (activationPoints > state.MaxActivationPoints)
        {
            errors.Add($"默认卡组需要 {activationPoints} 激活点，超过上限 {state.MaxActivationPoints}");
        }

        if (!division.CanEdit)
        {
            errors.Add(division.EditReason);
        }

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static int CalculateActivationPoints(DivisionWorkspaceData workspace, DivisionEditState state)
    {
        var curves = state.CostCurves.ToDictionary(
            item => NdfSyntaxDocument.Leaf(item.Category),
            item => item.Costs,
            StringComparer.Ordinal);
        var counts = state.DefaultDeck.GroupBy(
                item => workspace.Units.Units.FirstOrDefault(unit => unit.Name == item.Unit)?.Factory ?? string.Empty,
                StringComparer.Ordinal)
            .ToDictionary(group => NdfSyntaxDocument.Leaf(group.Key), group => group.Sum(item => Math.Max(0, item.Number)), StringComparer.Ordinal);
        var total = 0;
        foreach (var pair in counts.Where(item => item.Key.Length > 0))
        {
            if (!curves.TryGetValue(pair.Key, out var costs))
            {
                continue;
            }

            total += costs.Take(Math.Min(pair.Value, costs.Count)).Sum();
        }

        return total;
    }

    private static void ValidateAtom(string value, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => char.IsWhiteSpace(character) || character is '\'' or '"'))
        {
            errors.Add($"{label}不能为空或包含空白/引号");
        }
    }
}
