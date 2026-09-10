using System.Text.Json;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Strategic;

public sealed record StrategicSlot(string Id, string Pack, string Unit, string Transport, int Xp, int Count, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? Start = null);
public sealed record StrategicGroup(string Id, string Name, bool IsHQ, IReadOnlyList<StrategicSlot> Slots);
public sealed record StrategicCompany(string Id, string Name, bool IsHQ, IReadOnlyList<StrategicGroup> Groups);
public sealed record StrategicState(IReadOnlyList<StrategicCompany> Companies, IReadOnlyDictionary<string, string> PawnValues, string Name);
public sealed record StrategicSource(NdfObjectInfo Info, string Text);
public sealed record StrategicPack(StrategicSource Source, string Unit, string Transport, int Xp);
public sealed record StrategicField(string Key, string Label, string Constructor, string Field, string Kind);
public sealed record StrategicRecord(string Id, string DisplayName, StrategicSource Deck, StrategicSource? Pawn,
    StrategicState Baseline, IReadOnlyDictionary<string, string> Templates, string? Error)
{
    public string BattalionType { get; init; } = "";
    public bool HasCustomName { get; init; }
    public string Country {get;init;}="";
    public string Coalition {get;init;}="";
    public string Division {get;init;}="";
}

public sealed class StrategicWorkspace
{
    public required string Root { get; init; }
    public required UnitWorkspaceData Units { get; init; }
    public List<StrategicRecord> Records { get; } = [];
    public Dictionary<string, StrategicPack> Packs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, StrategicSource> CombatGroups { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, StrategicSource> Decks { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> GroupUsers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> DeckUsers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> CsvPaths { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, string>> Names { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, HashSet<string>> Choices { get; } = new(StringComparer.Ordinal);
    public List<string> Diagnostics { get; } = [];
    public bool HasRoster { get; set; }
    public string Name(string kind, string token) => Names.GetValueOrDefault(kind)?.GetValueOrDefault(token) ?? token;
}

public static class StrategicCodec
{
    public static string Serialize(StrategicState state) => JsonSerializer.Serialize(state);
    public static StrategicState Deserialize(string text) => JsonSerializer.Deserialize<StrategicState>(text)
        ?? throw new InvalidDataException("战略草稿为空");
    public static DraftOperation Operation(StrategicRecord record, StrategicState state) => new(
        DraftOperation.CreateId(DraftTargetKind.StrategicPlan, record.Deck.Info.RelativeSourceFile, record.Id, "strategic.plan"),
        null, DraftTargetKind.StrategicPlan, "strategic", record.Deck.Info.RelativeSourceFile, record.Id,
        record.Deck.Info.TypeName, "strategic.plan", "Strategic/Composition", "StrategicState",
        Serialize(record.Baseline), Serialize(record.Baseline), Serialize(state), Serialize(state),
        $"将军模式 · {record.DisplayName} · {string.Join("；", StrategicDiff.Compare(record.Baseline,state).Take(3))}",
        null, false, DateTimeOffset.UtcNow);
}

public static class StrategicFields
{
    public static readonly IReadOnlyList<StrategicField> All =
    [
        new("InitialActionPoint", "初始行动点", "TActionPointsModuleDescriptor", "InitialActionPoint", "number"),
        new("ActionPointRecoveryPerTurn", "每回合恢复行动点", "TActionPointsModuleDescriptor", "ActionPointRecoveryPerTurn", "number"),
        new("NbInitialActionsPointsForProducedPawn", "增援初始行动点", "TActionPointsModuleDescriptor", "NbInitialActionsPointsForProducedPawn", "number"),
        new("HasZoneOfControl", "控制区", "TStrategicBattleModuleDescriptor", "HasZoneOfControl", "bool"),
        new("CanBeInitialTarget", "可作为初始攻击目标", "TStrategicBattleModuleDescriptor", "CanBeInitialTarget", "bool"),
        new("BattleSupportRadiusInAPCase", "支援范围（行动点格）", "TStrategicBattleModuleDescriptor", "BattleSupportRadiusInAPCase", "radius"),
        new("BattleRole", "战斗角色", "TStrategicBattleModuleDescriptor", "BattleRole", "choice"),
        new("Movement", "移动类型", "", "", "choice"),
        new("InfluenceStrength", "战略影响强度", "TInfluenceMapModuleDescriptor", "InfluenceStrength", "number"),
        new("MinimumInfluenceStrength", "最低战略影响", "TInfluenceMapModuleDescriptor", "MinimumInfluenceStrength", "number"),
        new("StrengthDecayPerSecond", "影响衰减速度", "TInfluenceMapModuleDescriptor", "StrengthDecayPerSecond", "number"),
        new("PreventsDecayInZone", "区域内阻止衰减", "TInfluenceMapModuleDescriptor", "PreventsDecayInZone", "bool"),
        new("ProdMenuTexture", "棋子按钮图标", "StrategicUIModuleDescriptor", "ProdMenuTexture", "choice"),
        new("MapIcons", "地图图标组合", "TBUCKToolAlternativeValues_TUIValueTextureNameFromTEugBMutableInteger", "Values", "choice")
    ];
}

// All edits are scoped to syntax spans; source outside those spans stays byte-for-byte intact.
public static class StrategicSyntax
{
    public static NdfValueSpan? Field(NdfSyntaxDocument doc, string type, string field)
    {
        var constructors = doc.FindConstructors(type);
        if (constructors.Count != 1) return null;
        var values = doc.FindDirectAssignments(constructors[0], field);
        return values.Count == 1 ? values[0] : null;
    }
    public static string Read(string text, string type, string field, string fallback = "")
    {
        var doc = new NdfSyntaxDocument(text);
        var span = Field(doc, type, field);
        return span is null ? fallback : doc.Raw(span);
    }
    public static string Set(string text, string type, string field, string value, bool allowInsert = false, string? newLine = null)
    {
        var doc = new NdfSyntaxDocument(text);
        var span = Field(doc, type, field);
        if (span is not null)
            return SemicolonCsvDocument.ApplyReplacements(text, [new(doc.StartOffset(span), doc.Length(span), doc.Raw(span), value, field)]);
        var constructors = doc.FindConstructors(type);
        if (!allowInsert || constructors.Count != 1 || doc.FindDirectAssignments(constructors[0], field).Count != 0)
            throw new TransactionValidationException($"字段缺失或不唯一：{type}.{field}");
        var offset = doc.StartOffset(new(constructors[0].CloseTokenIndex, constructors[0].CloseTokenIndex));
        var nl = newLine ?? (text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n");
        return text.Insert(offset, $"    {field} = {value}{nl}");
    }
    public static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    public static string Leaf(string raw) => NdfSyntaxDocument.Leaf(NdfSyntaxDocument.Unquote(raw));
    public static string Array(IEnumerable<string> values, string nl) => "[" + nl + string.Concat(values.Select(v => "        " + v + "," + nl)) + "    ]";
}
