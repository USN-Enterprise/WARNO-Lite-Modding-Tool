namespace WarnoLiteModdingTool.Core.Units;

public sealed record UnitReferenceIndex(
    IReadOnlyDictionary<string, IReadOnlyList<string>> UnitWeapons,
    IReadOnlyDictionary<string, IReadOnlyList<string>> UnitAmmunition,
    IReadOnlyDictionary<string, IReadOnlyList<string>> UnitDivisions);
