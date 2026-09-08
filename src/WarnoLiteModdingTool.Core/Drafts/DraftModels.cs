namespace WarnoLiteModdingTool.Core.Drafts;

public sealed record DraftDocument(
    int SchemaVersion,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<DraftOperation> Operations)
{
    public static DraftDocument Empty { get; } = new(1, DateTimeOffset.MinValue, []);
}

public sealed record DraftOperation(
    string Id,
    string? GroupId,
    DraftTargetKind TargetKind,
    string Module,
    string RelativeSourceFile,
    string ObjectName,
    string ObjectType,
    string FieldKey,
    string FieldPath,
    string ValueType,
    string BaselineValue,
    string BaselineRaw,
    string TargetValue,
    string TargetRaw,
    string Summary,
    string? NameToken,
    bool RequiresNameTokenChange,
    DateTimeOffset UpdatedUtc,
    string? BaselineNameToken = null,
    DraftEditScope? EditScope = null,
    IReadOnlyList<string>? SelectedUnitNames = null,
    string? ContextWeaponName = null,
    int? ContextIndex = null)
{
    public static string CreateId(
        DraftTargetKind kind,
        string relativeSourceFile,
        string objectName,
        string fieldKey) =>
        $"{kind}|{Normalize(relativeSourceFile)}|{objectName}|{fieldKey}";

    private static string Normalize(string path) => path.Replace('\\', '/');
}

public enum DraftTargetKind
{
    NdfField,
    UnitName,
    WeaponField,
    MountedWeaponAmmo,
    AmmoField,
    UnitWeaponReference,
    DivisionPlan,
    OptionalUnitModule,
    StrategicPlan,
    UnitCreate,
    AmmoName,
    GlobalRule
}

public enum DraftEditScope
{
    CurrentUnit,
    SelectedUnits,
    AllReferences
}

public sealed record DraftLoadResult(
    DraftDocument Document,
    string? Error,
    bool IsBlocked);

public sealed record ResolvedDraftOperation(
    DraftOperation Operation,
    DraftResolutionStatus Status,
    string Reason);

public enum DraftResolutionStatus
{
    Active,
    Conflict
}
