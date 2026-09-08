namespace WarnoLiteModdingTool.Core.Projects;

public static class ProjectModuleCatalog
{
    public static IReadOnlyList<IProjectModule> CreateDefault() =>
    [
        new RuleProjectModule(),
        new FileSetProjectModule(
            "units",
            "单位",
            [
                Path.Combine("Gfx", "UniteDescriptor.ndf"),
                Path.Combine("Gfx", "BuildingDescriptors.ndf")
            ]),
        new FileSetProjectModule(
            "weapons",
            "武器",
            [Path.Combine("Gfx", "WeaponDescriptor.ndf")]),
        new FileSetProjectModule(
            "ammo",
            "弹药",
            [
                Path.Combine("Gfx", "Ammunition.ndf"),
                Path.Combine("Gfx", "AmmunitionMissiles.ndf")
            ]),
        new FileSetProjectModule(
            "divisions",
            "战术师",
            [
                Path.Combine("Decks", "Divisions.ndf"),
                Path.Combine("Decks", "DivisionRules.ndf"),
                Path.Combine("Decks", "DivisionCostMatrix.ndf"),
                Path.Combine("Decks", "DeckPacks.ndf"),
                Path.Combine("Decks", "Decks.ndf")
            ]),
        new FileSetProjectModule("strategic", "将军模式",
            [Path.Combine("Decks", "StrategicPacks.ndf"), Path.Combine("Decks", "StrategicDecks.ndf"),
             Path.Combine("Decks", "StrategicCombatGroups.ndf"), Path.Combine("Unit", "Strategic", "Units.ndf"),
             Path.Combine("Unit", "Strategic", "AirplaneUnits.ndf")])
    ];
}
