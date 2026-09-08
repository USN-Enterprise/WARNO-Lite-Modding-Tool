using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Localisation;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;

namespace WarnoLiteModdingTool.Core.Units;

public sealed class UnitProjectLoader(
    UnitCatalogBuilder? catalogBuilder = null,
    UnitLocalisationLoader? localisationLoader = null)
{
    private readonly UnitCatalogBuilder _catalogBuilder = catalogBuilder ?? new UnitCatalogBuilder();
    private readonly UnitLocalisationLoader _localisationLoader = localisationLoader ?? new UnitLocalisationLoader();
    private readonly NdfFieldLocator _locator = new();

    public Task<UnitWorkspaceData> LoadAsync(
        ModProjectContext context,
        ProjectIndexResult index,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(context, index, cancellationToken), cancellationToken);

    private UnitWorkspaceData Load(
        ModProjectContext context,
        ProjectIndexResult index,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in index.Objects.Select(item => item.SourceFile).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                sources[path] = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"无法读取 {Path.GetRelativePath(context.Layout.RootPath, path)}：{exception.Message}");
            }
        }

        var unitObjects = index.Objects.Where(item => item.ModuleKey == "units").ToArray();
        var units = _catalogBuilder.Build(unitObjects, sources, cancellationToken).ToArray();
        var damageResistance = DamageResistanceCatalog.Load(context);
        diagnostics.AddRange(damageResistance.Diagnostics);
        var localisation = _localisationLoader.Load(context);
        diagnostics.AddRange(localisation.Diagnostics);
        ReadNames(units, sources, localisation);
        UnitCatalogBuilder.ApplyChoices(units, damageResistance);

        var weaponAmmo = BuildWeaponAmmo(index, sources, cancellationToken);
        var unitWeapons = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var unitAmmo = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sources.TryGetValue(unit.Source.SourceFile, out var source))
            {
                continue;
            }

            var document = new NdfSyntaxDocument(source, unit.Source.CharacterOffset, unit.Source.CharacterLength);
            var weapons = document.FindReferenceLeaves("WeaponDescriptor_");
            unit.PresentationReferences = document.FindReferenceLeaves(string.Empty)
                .Where(item => item.Contains("MissileCarriage", StringComparison.OrdinalIgnoreCase) ||
                               item.Contains("Depiction", StringComparison.OrdinalIgnoreCase) ||
                               item.StartsWith("Modele_", StringComparison.Ordinal) ||
                               item.StartsWith("Texture_Button", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var ammo = weapons
                .SelectMany(weapon => weaponAmmo.TryGetValue(weapon, out var values) ? values : [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            unit.Weapons = weapons;
            unit.Ammunition = ammo;
            unitWeapons[unit.Name] = weapons;
            unitAmmo[unit.Name] = ammo;
        }

        var divisions = BuildDivisionUnits(index, sources, cancellationToken);
        var unitDivisions = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            var matching = divisions
                .Where(pair => pair.Value.Contains(unit.Name, StringComparer.Ordinal))
                .Select(pair => pair.Key)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            unit.Divisions = matching;
            unitDivisions[unit.Name] = matching;
        }

        return new UnitWorkspaceData(
            units,
            new UnitReferenceIndex(unitWeapons, unitAmmo, unitDivisions),
            localisation,
            damageResistance,
            diagnostics) { Rules = Rules.RuleWorkspace.Load(context.Layout.RootPath) };
    }

    private void ReadNames(
        IEnumerable<UnitRecord> units,
        IReadOnlyDictionary<string, string> sources,
        UnitLocalisationCatalog localisation)
    {
        var unitArray = units.ToArray();
        var tokens = new List<string>();
        var uniqueNameFields = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var unit in unitArray)
        {
            if (!sources.TryGetValue(unit.Source.SourceFile, out var source))
            {
                continue;
            }

            var document = new NdfSyntaxDocument(source, unit.Source.CharacterOffset, unit.Source.CharacterLength);
            var match = _locator.Locate(document, new NdfFieldSelector("TUnitUIModuleDescriptor", "NameToken"));
            uniqueNameFields[unit.Name] = match.Values.Count == 1;
            if (match.Values.Count == 1)
            {
                unit.NameToken = NdfSyntaxDocument.Unquote(document.Raw(match.Values[0]));
                if (!string.IsNullOrWhiteSpace(unit.NameToken))
                {
                    tokens.Add(unit.NameToken);
                }
            }

            if (localisation.TryResolve(unit.NameToken, out var localised))
            {
                unit.DisplayName = localised;
                unit.NameSource = "UNITS.csv";
            }
            else
            {
                unit.DisplayName = unit.Source.DisplayName;
                unit.NameSource = localisation.IsTokenAmbiguous(unit.NameToken)
                    ? "token 重复，使用内部名称"
                    : "内部描述符";
            }

            unit.UnitsCsvRelativePath = localisation.UniqueUnitsCsvPath is null
                ? null
                : Path.GetRelativePath(localisation.ProjectRoot, localisation.UniqueUnitsCsvPath);
        }

        localisation.AddKnownTokens(tokens);
        var tokenCounts = unitArray
            .Where(unit => !string.IsNullOrWhiteSpace(unit.NameToken))
            .GroupBy(unit => unit.NameToken!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var unit in unitArray)
        {
            var ndfTokenDuplicate = unit.NameToken is not null &&
                tokenCounts.TryGetValue(unit.NameToken, out var count) && count > 1;
            unit.NameTokenRequiresReplacement = unit.NameToken?.Length != 10 || ndfTokenDuplicate;
            unit.CanEditName = localisation.UniqueUnitsCsvPath is not null &&
                uniqueNameFields.GetValueOrDefault(unit.Name) &&
                !localisation.IsTokenAmbiguous(unit.NameToken);
            unit.NameEditReason = unit.CanEditName
                ? string.Empty
                : localisation.UniqueUnitsCsvPath is null
                    ? "没有唯一可解析的 UNITS.csv 目标"
                    : localisation.IsTokenAmbiguous(unit.NameToken)
                        ? "NameToken 在 UNITS.csv 中重复"
                        : ndfTokenDuplicate
                            ? "NameToken 被多个 Unit 共用"
                            : "NameToken 字段缺失或多义";
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildWeaponAmmo(
        ProjectIndexResult index,
        IReadOnlyDictionary<string, string> sources,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var weapon in index.Objects.Where(item => item.ModuleKey == "weapons"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sources.TryGetValue(weapon.SourceFile, out var source))
            {
                var document = new NdfSyntaxDocument(source, weapon.CharacterOffset, weapon.CharacterLength);
                result[weapon.Name] = document.FindReferenceLeaves("Ammo_");
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildDivisionUnits(
        ProjectIndexResult index,
        IReadOnlyDictionary<string, string> sources,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var division in index.Objects.Where(item =>
                     item.ModuleKey == "divisions" && item.TypeName.Contains("DivisionRule", StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sources.TryGetValue(division.SourceFile, out var source))
            {
                var document = new NdfSyntaxDocument(source, division.CharacterOffset, division.CharacterLength);
                result[division.DisplayName] = document.FindReferenceLeaves("Descriptor_Unit_");
            }
        }

        return result;
    }
}
