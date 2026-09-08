using WarnoLiteModdingTool.Core.Localisation;

namespace WarnoLiteModdingTool.Core.Units;

public sealed record UnitWorkspaceData(
    IReadOnlyList<UnitRecord> Units,
    UnitReferenceIndex References,
    UnitLocalisationCatalog Localisation,
    DamageResistanceCatalog DamageResistance,
    IReadOnlyList<string> Diagnostics)
{
    public Rules.RuleWorkspace? Rules { get; init; }
}
