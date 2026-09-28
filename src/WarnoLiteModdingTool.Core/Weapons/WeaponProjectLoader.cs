using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

public sealed class WeaponProjectLoader
{
    private static readonly string[] TurretTypes =
    [
        "TTurretTwoAxisDescriptor",
        "TTurretUnitDescriptor",
        "TTurretInfanterieDescriptor",
        "TTurretBombardierDescriptor"
    ];

    public Task<WeaponWorkspaceData> LoadAsync(
        ModProjectContext context,
        ProjectIndexResult index,
        UnitWorkspaceData units,
        CancellationToken cancellationToken = default,
        ProjectLoadCache? cache = null) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cache?.RestoreWeapons(units) is { } restored) { cache.SetWeapons(restored); return restored; }
            var result = Load(context, index, units, cancellationToken);
            if (result.Diagnostics.Count == 0) cache?.SetWeapons(result);
            return result;
        }, cancellationToken);

    private static WeaponWorkspaceData Load(
        ModProjectContext context,
        ProjectIndexResult index,
        UnitWorkspaceData units,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<string>();
        var damageResistance = units.DamageResistance;
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in index.Objects
                     .Where(item => item.ModuleKey is "weapons" or "ammo")
                     .Select(item => item.SourceFile)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                sources[path] = WarnoLiteModdingTool.Core.Projects.ProjectReadScope.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"无法读取 {Path.GetRelativePath(context.Layout.RootPath, path)}：{exception.Message}");
            }
        }

        var ammo = new List<AmmoRecord>();
        foreach (var descriptor in index.Objects.Where(item => item.ModuleKey == "ammo" && item.TypeName == "TAmmunitionDescriptor"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sources.TryGetValue(descriptor.SourceFile, out var source))
            {
                continue;
            }

            var document = new NdfSyntaxDocument(source, descriptor.CharacterOffset, descriptor.CharacterLength);
            var roots = document.FindConstructors(descriptor.TypeName);
            if (roots.Count != 1)
            {
                diagnostics.Add($"Ammo 根对象无法唯一定位：{descriptor.Name}");
                continue;
            }

            var fields = new List<WeaponFieldValue>();
            foreach (var definition in WeaponFieldDefinitions.Ammo)
            {
                var values = LocateAmmoField(document, roots[0], definition);
                if (values.Count != 1)
                {
                    if (definition.Professional || definition.CanInsert) fields.Add(values.Count == 0 ? AmmoProfessional.Missing(descriptor, source, document, roots[0], definition)
                        : AmmoProfessional.Unavailable(descriptor, definition, "", "字段重复，无法唯一定位"));
                    continue;
                }

                var raw = document.Raw(values[0]);
                if (!WeaponValueConverter.TryRead(definition, raw, out var display))
                {
                    if (definition.Professional || definition.CanInsert) fields.Add(AmmoProfessional.Unavailable(descriptor, definition, raw, "原值格式暂不支持"));
                    continue;
                }

                var choices = definition.Key == "ammo.damage.family"
                    ? damageResistance.DamageFamilies.Select(item => item.Name).Append(raw).Distinct(StringComparer.Ordinal).ToArray()
                    : [];
                fields.Add(CreateValue(descriptor, source, document, definition, raw, display, values[0], choices));
            }

            var record=new AmmoRecord(descriptor,fields);var nameValues=document.FindDirectAssignments(roots[0],"Name");
            record.DisplayName=descriptor.Name;record.ChineseName=descriptor.Name;
            if(nameValues.Count==1){var span=nameValues[0];record.NameRaw=document.Raw(span);record.NameToken=NdfSyntaxDocument.Unquote(record.NameRaw);var local=units.Localisation.TryResolve(record.NameToken,out var custom);record.DisplayName=local?custom:Localisation.VanillaNames.Lookup("UNITS",record.NameToken)??descriptor.Name;record.ChineseName=local?custom:Localisation.VanillaNames.Lookup("UNITS",record.NameToken,"SC")??record.DisplayName;
                if(string.IsNullOrWhiteSpace(record.DisplayName))record.DisplayName=descriptor.Name;if(string.IsNullOrWhiteSpace(record.ChineseName))record.ChineseName=record.DisplayName;
                record.NameLocation=CreateValue(descriptor,source,document,new("ammo.name","名称","游戏内名称","",WeaponFieldOwner.Ammo,"Name",WeaponValueKind.Text),record.NameRaw,record.DisplayName,span,[]).Location;
                record.CanEditName=units.Localisation.UniqueUnitsCsvPath is not null&&record.NameRaw.Length>=2&&record.NameRaw[0] is '\'' or '"';}
            ammo.Add(record);
        }

        var catalogs = ammo.SelectMany(a => a.Fields).Where(f => f.State == WeaponFieldState.Declared && f.Definition.ValueKind == WeaponValueKind.CatalogChoice)
            .GroupBy(f => f.Key).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(f => f.RawValue).Distinct().Order(StringComparer.Ordinal).ToArray());
        var tags = ammo.SelectMany(a => a.Fields).Where(f => f.State == WeaponFieldState.Declared && f.Definition.ValueKind == WeaponValueKind.Tags)
            .SelectMany(f => AmmoProfessional.Tags(f.RawValue)).Distinct().Order(StringComparer.Ordinal).ToArray();
        IReadOnlyList<string> catalogFiles = [];
        IReadOnlyList<AmmoReferenceCatalog.Missile> missileCatalog = [];
        if (ammo.Count > 0)
            foreach (var catalog in AmmoReferenceCatalog.Load(context.Layout.RootPath, cancellationToken, out catalogFiles, out missileCatalog)) catalogs[catalog.Key] = catalog.Value;
        for (var i = 0; i < ammo.Count; i++)
        {
            var a = ammo[i];
            WeaponFieldValue WithChoices(WeaponFieldValue f)
            {
                if (f.Definition.ValueKind == WeaponValueKind.Tags) return f with { Choices = tags };
                if (f.Definition.ValueKind != WeaponValueKind.CatalogChoice) return f;
                var candidates = catalogs.GetValueOrDefault(f.Key) ?? [];
                if (f.Definition.FieldName == "MissileDescriptor") candidates = candidates.Concat(missileCatalog.Where(m =>
                    m.File.Equals(a.Source.RelativeSourceFile.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) || m.Exported &&
                    string.Equals(Path.GetDirectoryName(m.File.Replace('\\', '/')), Path.GetDirectoryName(a.Source.RelativeSourceFile.Replace('\\', '/')), StringComparison.OrdinalIgnoreCase)).Select(m => "~/" + m.Name)).ToArray();
                var choices = candidates.Concat(f.State == WeaponFieldState.Declared ? new[] { f.RawValue } : []).Distinct().ToArray();
                return f with { Choices = choices, State = f.IsMissing && choices.Length == 0 ? WeaponFieldState.Unavailable : f.State,
                    Reason = f.IsMissing && choices.Length == 0 ? "当前Mod没有可用候选，保留未声明" : f.Reason };
            }
            ammo[i] = new AmmoRecord(a.Source, a.Fields.Select(WithChoices).ToArray())
                { NameToken = a.NameToken, NameRaw = a.NameRaw, NameLocation = a.NameLocation, CanEditName = a.CanEditName, DisplayName = a.DisplayName, ChineseName = a.ChineseName };
        }
        var ammoChoices = ammo.Select(item => item.Name).Order(StringComparer.Ordinal).ToArray();
        units.Localisation.AddKnownTokens(ammo.Select(a=>a.NameToken));
        var weapons = new List<WeaponRecord>();
        foreach (var descriptor in index.Objects.Where(item => item.ModuleKey == "weapons"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sources.TryGetValue(descriptor.SourceFile, out var source))
            {
                continue;
            }

            var document = new NdfSyntaxDocument(source, descriptor.CharacterOffset, descriptor.CharacterLength);
            var roots = document.FindConstructors(descriptor.TypeName);
            if (roots.Count != 1)
            {
                diagnostics.Add($"Weapon 根对象无法唯一定位：{descriptor.Name}");
                continue;
            }

            var fields = new List<WeaponFieldValue>();
            var salves = document.FindDirectAssignments(roots[0], "Salves");
            if (salves.Count == 1)
            {
                var elements = document.ReadArrayElements(salves[0]);
                for (var indexValue = 0; indexValue < elements.Count; indexValue++)
                {
                    var definition = WeaponFieldDefinitions.Salves(indexValue);
                    var raw = document.Raw(elements[indexValue]);
                    if (WeaponValueConverter.TryRead(definition, raw, out var display))
                    {
                        fields.Add(CreateValue(descriptor, source, document, definition, raw, display, elements[indexValue], []));
                    }
                }
            }

            var mounts = new List<MountedWeaponRecord>();
            var turretIndex = 0;
            foreach (var turretType in TurretTypes)
            {
                foreach (var turret in document.FindConstructors(turretType).OrderBy(item => document.StartOffset(new NdfValueSpan(item.TypeTokenIndex, item.CloseTokenIndex))))
                {
                    AddTurretFields(descriptor, source, document, turret, turretIndex, fields);
                    var lists = document.FindDirectAssignments(turret, "MountedWeaponDescriptorList");
                    if (lists.Count == 1)
                    {
                        foreach (var mount in document.FindConstructors("TMountedWeaponDescriptor", lists[0]))
                        {
                            mounts.Add(ReadMount(descriptor, source, document, mount, mounts.Count, turretIndex, turretType, ammoChoices));
                        }
                    }

                    turretIndex++;
                }
            }

            mounts.Sort((left, right) => left.Index.CompareTo(right.Index));
            weapons.Add(new WeaponRecord(descriptor, fields, mounts));
        }

        return new WeaponWorkspaceData(weapons, ammo, units.Units, BuildReferences(weapons, ammo, units.Units), diagnostics) { CatalogFiles = catalogFiles };
    }

    internal static WeaponReferenceIndex BuildReferences(IReadOnlyList<WeaponRecord> weapons, IReadOnlyList<AmmoRecord> ammo, IReadOnlyList<UnitRecord> units)
    {
        var byWeapon = units.SelectMany(u => u.Weapons.Select(w => (Weapon: w, Unit: u.Name)))
            .GroupBy(p => p.Weapon, StringComparer.Ordinal).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(p => p.Unit).Distinct().Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var byAmmo = weapons.SelectMany(w => w.Mounts.Select(m => (Ammo: m.AmmoName, Weapon: w.Name)))
            .GroupBy(p => p.Ammo, StringComparer.Ordinal).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(p => p.Weapon).Distinct().Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var weaponUnits = weapons.ToDictionary(
            weapon => weapon.Name,
            weapon => byWeapon.GetValueOrDefault(weapon.Name) ?? [],
            StringComparer.Ordinal);
        var ammoWeapons = ammo.ToDictionary(
            item => item.Name,
            item => byAmmo.GetValueOrDefault(item.Name) ?? [],
            StringComparer.Ordinal);
        var ammoUnits = ammo.ToDictionary(
            item => item.Name,
            item => (IReadOnlyList<string>)ammoWeapons[item.Name]
                .SelectMany(weapon => weaponUnits.GetValueOrDefault(weapon) ?? [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);

        return new WeaponReferenceIndex(weaponUnits, ammoWeapons, ammoUnits);
    }

    public static AmmoRecord ParseWeaponAmmo(NdfObjectInfo descriptor, string body, WeaponWorkspaceData data)
    {
        var document = new NdfSyntaxDocument(body); var root = document.FindConstructors(descriptor.TypeName).Single();
        var fields = new List<WeaponFieldValue>();
        foreach (var definition in WeaponFieldDefinitions.Ammo)
        {
            var spans = LocateAmmoField(document, root, definition);
            if (spans.Count != 1) { if (definition.Professional || definition.CanInsert) fields.Add(spans.Count == 0 ? AmmoProfessional.Missing(descriptor, body, document, root, definition)
                : AmmoProfessional.Unavailable(descriptor, definition, "", "字段重复，无法唯一定位")); continue; }
            var raw = document.Raw(spans[0]);
            if (!WeaponValueConverter.TryRead(definition, raw, out var value)) { if (definition.Professional || definition.CanInsert) fields.Add(AmmoProfessional.Unavailable(descriptor, definition, raw, "原值格式暂不支持")); continue; }
            fields.Add(CreateValue(descriptor, body, document, definition, raw, value, spans[0], data.Ammo(descriptor.Name)?.Field(definition.Key)?.Choices ?? []));
        }
        return new(descriptor, fields.Select(f => f with { Choices = data.Ammo(descriptor.Name)?.Field(f.Key)?.Choices ?? f.Choices }).ToArray());
    }

    private static IReadOnlyList<NdfValueSpan> LocateAmmoField(
        NdfSyntaxDocument document,
        NdfConstructorSpan root,
        WeaponFieldDefinition definition)
    {
        if (definition.Constructor is not null)
        {
            var owner = AmmoProfessional.Owner(document, root, definition);
            return owner is null ? [] : document.FindDirectAssignments(owner, definition.FieldName);
        }
        if (definition.MapKey is not null)
        {
            var hitRoll = document.FindDirectAssignments(root, "HitRollRuleDescriptor");
            if (hitRoll.Count != 1)
            {
                return [];
            }

            var dice = document.FindConstructors("TDiceHitRollRuleDescriptor", hitRoll[0]);
            if (dice.Count != 1)
            {
                return [];
            }

            var map = document.FindDirectAssignments(dice[0], definition.FieldName);
            if (map.Count != 1)
            {
                return [];
            }

            return document.ReadMapEntries(map[0])
                .Where(item => string.Equals(NdfSyntaxDocument.Leaf(NdfSyntaxDocument.Unquote(document.Raw(item.Key))), NdfSyntaxDocument.Leaf(definition.MapKey), StringComparison.Ordinal))
                .Select(item => item.Value)
                .ToArray();
        }

        var values = document.FindDirectAssignments(root, definition.FieldName);
        if (definition.ArgumentName is null)
        {
            return values;
        }

        return values.Select(value => document.FindNamedArgument(value, definition.ArgumentName))
            .Where(value => value is not null)
            .Cast<NdfValueSpan>()
            .ToArray();
    }

    private static void AddTurretFields(
        NdfObjectInfo descriptor,
        string source,
        NdfSyntaxDocument document,
        NdfConstructorSpan turret,
        int turretIndex,
        ICollection<WeaponFieldValue> fields)
    {
        var definitions = new[]
        {
            WeaponFieldDefinitions.Turret(turretIndex, "AngleRotationMax", "最大水平角"),
            WeaponFieldDefinitions.Turret(turretIndex, "AngleRotationMaxPitch", "最大俯仰角"),
            WeaponFieldDefinitions.Turret(turretIndex, "AngleRotationMinPitch", "最小俯仰角"),
            WeaponFieldDefinitions.Turret(turretIndex, "VitesseRotation", "旋转速度")
        };
        foreach (var definition in definitions)
        {
            var values = document.FindDirectAssignments(turret, definition.FieldName);
            if (values.Count != 1)
            {
                continue;
            }

            var raw = document.Raw(values[0]);
            if (WeaponValueConverter.TryRead(definition, raw, out var display))
            {
                fields.Add(CreateValue(descriptor, source, document, definition, raw, display, values[0], []));
            }
        }
    }

    private static MountedWeaponRecord ReadMount(
        NdfObjectInfo descriptor,
        string source,
        NdfSyntaxDocument document,
        NdfConstructorSpan mount,
        int mountIndex,
        int turretIndex,
        string turretType,
        IReadOnlyList<string> ammoChoices)
    {
        var fields = new List<WeaponFieldValue>();
        var ammo = Direct(document, mount, "Ammunition");
        var ammoName = ammo is null ? string.Empty : NdfSyntaxDocument.Leaf(NdfSyntaxDocument.Unquote(document.Raw(ammo)));
        if (ammo is not null)
        {
            var definition = WeaponFieldDefinitions.MountedAmmo(mountIndex);
            fields.Add(CreateValue(descriptor, source, document, definition, document.Raw(ammo), ammoName, ammo, ammoChoices));
        }

        var count = Direct(document, mount, "NbWeapons");
        if (count is not null)
        {
            var definition = WeaponFieldDefinitions.MountedCount(mountIndex);
            var raw = document.Raw(count);
            if (WeaponValueConverter.TryRead(definition, raw, out var display))
                fields.Add(CreateValue(descriptor, source, document, definition, raw, display, count, []));
        }
        var hidden = Direct(document, mount, "HideInInterface");
        if (hidden is not null)
        {
            var definition = WeaponFieldDefinitions.MountedHidden(mountIndex);
            var raw = document.Raw(hidden);
            if (WeaponValueConverter.TryRead(definition, raw, out var display))
            {
                fields.Add(CreateValue(descriptor, source, document, definition, raw, display, hidden, ["是", "否"]));
            }
        }

        var ammoBoxRaw = RawDirect(document, mount, "AmmoBoxIndex");
        int? ammoBox = int.TryParse(ammoBoxRaw, out var value) ? value : null;
        var effect = NdfSyntaxDocument.Unquote(RawDirect(document, mount, "EffectTag"));
        var alternative = NdfSyntaxDocument.Unquote(RawDirect(document, mount, "HandheldEquipmentKey"));
        var animations = string.Join(", ", new[]
        {
            RawDirect(document, mount, "WeaponActiveAndCanShootPropertyName"),
            RawDirect(document, mount, "WeaponIgnoredPropertyName"),
            RawDirect(document, mount, "WeaponShootDataPropertyName")
        }.Where(item => item.Length > 0));
        var presentation = document.FindReferenceLeaves(string.Empty)
            .Where(item => item.Contains("Texture", StringComparison.OrdinalIgnoreCase) || item.Contains("Depiction", StringComparison.OrdinalIgnoreCase) || item.Contains("MissileCarriage", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToArray();
        return new MountedWeaponRecord(
            mountIndex,
            turretIndex,
            turretType,
            ammoBox,
            ammoName,
            fields,
            effect,
            alternative,
            animations,
            string.Join(", ", presentation));
    }

    private static NdfValueSpan? Direct(NdfSyntaxDocument document, NdfConstructorSpan constructor, string field)
    {
        var values = document.FindDirectAssignments(constructor, field);
        return values.Count == 1 ? values[0] : null;
    }

    private static string RawDirect(NdfSyntaxDocument document, NdfConstructorSpan constructor, string field)
    {
        var value = Direct(document, constructor, field);
        return value is null ? string.Empty : document.Raw(value);
    }

    private static WeaponFieldValue CreateValue(
        NdfObjectInfo descriptor,
        string source,
        NdfSyntaxDocument document,
        WeaponFieldDefinition definition,
        string raw,
        string display,
        NdfValueSpan span,
        IReadOnlyList<string> choices)
    {
        var offset = document.StartOffset(span);
        var line = descriptor.LineNumber + source.AsSpan(descriptor.CharacterOffset, offset - descriptor.CharacterOffset).Count('\n');
        return new WeaponFieldValue(
            definition,
            descriptor.Name,
            descriptor.TypeName,
            display,
            raw,
            new WeaponFieldLocation(descriptor.RelativeSourceFile, definition.Constructor is null ? $"{descriptor.TypeName}.{definition.FieldName}" : AmmoProfessional.Path(definition), offset, document.Length(span), line),
            choices);
    }
}
