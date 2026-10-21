using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.App.ViewModels.Units;
using WarnoLiteModdingTool.App.ViewModels.Weapons;
using WarnoLiteModdingTool.App.ViewModels.Divisions;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.SourceWorkspace;
using WarnoLiteModdingTool.Core.Strategic;
using WarnoLiteModdingTool.Core.Weapons;
using WarnoLiteModdingTool.Core.Rules;

namespace WarnoLiteModdingTool.App.SourceWorkspace;

public sealed record BusinessSourceTarget(string Path, string Label, Func<string, IReadOnlyList<SourceRange>> Locate, bool Shared = false);
public sealed record RuleSourceTarget(RuleGroupViewModel Group, RuleCellViewModel? Cell = null);
public sealed record ExperienceSourceTarget(ExperienceLevelViewModel Level, ExperienceCellViewModel Cell);

public static class BusinessSourceTargets
{
    public static BusinessSourceTarget Object(NdfObjectInfo source, bool shared = false) =>
        new(source.RelativeSourceFile, source.Name, text => SourceNavigation.Object(text, source.Name, source.TypeName), shared);

    public static IReadOnlyList<BusinessSourceTarget> Create(MainViewModel main, object? item)
    {
        switch (item)
        {
            case BusinessSourceTarget target: return [target];
            case NdfObjectInfo source: return [Object(source)];
            case UnitListItemViewModel unit: return [Object(unit.Unit.Source)];
            case UnitFieldViewModel field:
                if (field.IsName)
                {
                    var names = new List<BusinessSourceTarget> { new(field.Unit.Source.RelativeSourceFile, "NameToken · NDF", text => SourceNavigation.Direct(text, field.Unit.Name, field.Unit.Source.TypeName, "NameToken")) };
                    if (field.Unit.UnitsCsvRelativePath is {} csv && field.Unit.NameToken is {} token) names.Add(new(csv, token + " · CSV", text => SourceNavigation.Csv(text, token)));
                    return names;
                }
                if (field.Field is not {} value) return [];
                return [new(field.Unit.Source.RelativeSourceFile, value.Definition.Selector.DisplayPath,
                    text => SourceNavigation.UnitField(text, field.Unit.Name, field.Unit.Source.TypeName, value.Definition.Selector))];
            case AmmoListItemViewModel ammo: return [Object(ammo.Ammo.Source, true)];
            case WeaponRecord weapon: return [Object(weapon.Source, true)];
            case WeaponMountItemViewModel mount when main.WeaponWorkspace?.SelectedWeapon is {} weapon:
                return [new(weapon.Source.RelativeSourceFile, mount.Header + " · " + mount.Mount.AmmoName,
                    text => SourceNavigation.Mount(text, weapon.Name, weapon.Source.TypeName, mount.Mount.AmmoName, mount.Mount.AmmoBoxIndex), true)];
            case WeaponFieldViewModel field:
                var f = field.Field;
                var selectedMount = main.WeaponWorkspace?.SelectedMount?.Mount;
                var locations = new List<BusinessSourceTarget> { new(f.Location.RelativeSourceFile, f.Location.FieldPath,
                    text => SourceNavigation.WeaponField(text, f.OwnerObjectName, f.OwnerObjectType, f.Definition, selectedMount), true) };
                if (field.MissileEntity is { } missile) locations.Add(Object(missile, true));
                return locations;
            case DivisionListItemViewModel division: return [Object(division.Division.Source)];
            case DivisionUnitRuleViewModel rule when main.DivisionWorkspace?.SelectedDivision is {} division:
                return [new(division.Division.UnitRuleList.RelativeSourceFile, rule.Unit,
                    text => SourceNavigation.DivisionMember(text, division.Division.DivisionRuleName, rule.Unit))];
            case DivisionPackViewModel pack:
                return main.ProjectObjects.Where(o => o.Name == pack.OriginalPackName && o.TypeName == "DeckPackDescriptor").Select(o => Object(o, true)).ToArray();
            case StrategicRecord strategic:
                return strategic.Pawn is {} pawn ? [Object(pawn.Info), Object(strategic.Deck.Info, true)] : [Object(strategic.Deck.Info, true)];
            case PackListItem pack: return [Object(pack.Source.Source.Info, true)];
            case StrategicNode node when main.StrategicWorkspace is {} strategic:
                return StrategicTargets(strategic, node);
            case RuleSourceTarget rule:
                var definition = rule.Group.Group.Definition;
                if (definition.Number == AirLayout.Number) return AirTargets();
                return [new(definition.RelativePath, rule.Group.Title + (rule.Cell is {} cell ? " · " + cell.Cell.Label : ""),
                    text => SourceNavigation.Rule(text, definition, rule.Cell?.Cell.Key), true)];
            case TerrainViewModel terrain:
                return [new(terrain.Terrain.File, terrain.Terrain.Name, text => SourceNavigation.Terrain(text, terrain.Terrain.Name, null), true)];
            case TerrainFieldViewModel terrain:
                return [new(terrain.Terrain.File, terrain.Cell.Key, text => SourceNavigation.Terrain(text, terrain.Terrain.Name, terrain.Cell.Key), true)];
            case ExperienceSourceTarget experience:
                var ec = experience.Cell.Cell;
                return [new(ec.File, ec.Source + " · " + ec.Field, text => SourceNavigation.Experience(text, experience.Level.Route, experience.Level.Level, ec), true)];
            case ExperienceLevelViewModel level:
                return level.Cells.SelectMany(c => Create(main, new ExperienceSourceTarget(level, c))).ToArray();
            default: return [];
        }
    }

    private static IReadOnlyList<BusinessSourceTarget> StrategicTargets(StrategicWorkspaceViewModel vm, StrategicNode node)
    {
        if (node.Kind == "root" && vm.Selected is {} root) return root.Pawn is {} pawn ? [Object(pawn.Info), Object(root.Deck.Info, true)] : [Object(root.Deck.Info, true)];
        var owner = node.Id.Split('/')[0];
        if (!vm.Data.CombatGroups.TryGetValue(owner, out var source) || vm.Selected is not {} selected) return [];
        if (node.Kind == "company") return [Object(source.Info, true)];
        if (node.Kind == "group" && selected.Templates.TryGetValue(node.Id, out var body))
            return [new(source.Info.RelativeSourceFile, node.Id, text => SourceNavigation.Fragment(text, owner, body, node.Id), true)];
        if (node.Kind != "unit") return [];
        var baseline = selected.Baseline.Companies.SelectMany(c => c.Groups).SelectMany(g => g.Slots).FirstOrDefault(s => s.Id == node.Id);
        if (baseline is null || !selected.Templates.TryGetValue(node.Id + "/start", out var start)) return [];
        var tuple = "(" + start + ", " + baseline.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        var targets = new List<BusinessSourceTarget> { new(source.Info.RelativeSourceFile, node.Id + " · PackIndex", text => SourceNavigation.Fragment(text, owner, tuple, node.Id), true) };
        if (vm.Data.Packs.TryGetValue(baseline.Pack, out var pack)) targets.Add(Object(pack.Source.Info, true));
        return targets;
    }

    private static IReadOnlyList<BusinessSourceTarget> AirTargets() =>
    [
        new(AirLayout.Directory + AirLayout.Files[0], "NbMaxPlanes", text => NamedField(text, "TUISpecificSkirmishProductionMenuViewDescriptor", "NbMaxPlanes"), true),
        new(AirLayout.Directory + AirLayout.Files[1], "UnitGrid · MaxElementsPerDimension", text => NamedField(text, "BUCKGridDescriptor", "MaxElementsPerDimension", "UnitGrid"), true),
        new(AirLayout.Directory + AirLayout.Files[1], "UnitGrid · GridElements", text => NamedField(text, "BUCKGridDescriptor", "GridElements", "UnitGrid"), true),
        new(AirLayout.Directory + AirLayout.Files[2], "OffMapAirplaneComponentDimension", text => { var doc = new NdfSyntaxDocument(text); return doc.FindNamedValues("OffMapAirplaneComponentDimension").Select(v => new SourceRange(doc.StartOffset(v), doc.Length(v), "OffMapAirplaneComponentDimension")).ToArray(); }, true)
    ];

    private static IReadOnlyList<SourceRange> NamedField(string text, string type, string field, string? element = null)
    {
        var doc = new NdfSyntaxDocument(text);
        return doc.FindConstructors(type).Where(c => element is null || doc.FindDirectAssignments(c, "ElementName").Any(v => NdfSyntaxDocument.Unquote(doc.Raw(v)) == element))
            .SelectMany(c => doc.FindDirectAssignments(c, field)).Select(v => new SourceRange(doc.StartOffset(v), doc.Length(v), field)).ToArray();
    }
}
