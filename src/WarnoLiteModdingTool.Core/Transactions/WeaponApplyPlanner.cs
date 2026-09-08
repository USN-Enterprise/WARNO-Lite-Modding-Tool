using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.Core.Transactions;

public sealed record WeaponPlannedReplacement(string RelativePath, TextReplacement Replacement, string Summary);

public sealed record WeaponApplyPlan(
    IReadOnlyList<WeaponPlannedReplacement> Replacements,
    IReadOnlyDictionary<string, IReadOnlyList<string>> NewObjectsByFile,
    IReadOnlyList<string> ValidationMessages);

public sealed class WeaponApplyPlanner
{
    private sealed class CloneTarget(NdfObjectInfo source, string name)
    {
        public NdfObjectInfo Source { get; } = source;
        public string Name { get; } = name;
        public List<TextReplacement> RelativeReplacements { get; } = [];
    }

    public WeaponApplyPlan Plan(
        string root,
        UnitWorkspaceData units,
        WeaponWorkspaceData workspace,
        IReadOnlyList<DraftOperation> operations,
        Func<string, TextFileSnapshot> getSnapshot)
    {
        var p4 = operations.Where(IsP4).ToArray();
        if (p4.Length == 0)
        {
            return new WeaponApplyPlan([], new Dictionary<string, IReadOnlyList<string>>(), []);
        }

        var result = new List<WeaponPlannedReplacement>();
        var replacementKeys = new HashSet<string>(StringComparer.Ordinal);
        var ammoClones = new Dictionary<string, CloneTarget>(StringComparer.Ordinal);
        var weaponClones = new Dictionary<string, CloneTarget>(StringComparer.Ordinal);
        var existingNames = workspace.Weapons.Select(item => item.Name)
            .Concat(workspace.Ammunition.Select(item => item.Name))
            .ToHashSet(StringComparer.Ordinal);

        void AddDirect(string relative, TextReplacement replacement, string summary)
        {
            var key = $"{Normalize(relative)}|{replacement.Offset}|{replacement.Length}";
            if (replacementKeys.Add(key))
            {
                result.Add(new WeaponPlannedReplacement(Normalize(relative), replacement, summary));
                return;
            }

            var existing = result.Single(item => $"{item.RelativePath}|{item.Replacement.Offset}|{item.Replacement.Length}" == key);
            if (!string.Equals(existing.Replacement.Target, replacement.Target, StringComparison.Ordinal))
            {
                throw new TransactionValidationException($"多个草稿对同一字段给出不同目标：{summary}");
            }
        }

        foreach (var operation in p4)
        {
            if (operation.TargetKind == DraftTargetKind.UnitWeaponReference)
            {
                PlanUnitWeaponReplacement(operation, units, workspace, getSnapshot, AddDirect);
                continue;
            }

            var scope = operation.EditScope ?? DraftEditScope.CurrentUnit;
            var selected = scope == DraftEditScope.AllReferences
                ? []
                : operation.SelectedUnitNames?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                    ?? throw new TransactionValidationException($"草稿缺少作用域 Unit：{operation.Summary}");

            if (operation.TargetKind == DraftTargetKind.AmmoField)
            {
                var ammo = workspace.Ammo(operation.ObjectName)
                    ?? throw new TransactionValidationException($"Ammo 不存在：{operation.ObjectName}");
                var field = ammo.Field(operation.FieldKey)
                    ?? throw new TransactionValidationException($"Ammo 字段不存在：{operation.FieldKey}");
                if (scope == DraftEditScope.AllReferences)
                {
                    AddFieldDirect(field, operation, AddDirect);
                    continue;
                }

                var ammoKey = IsolationKey(ammo.Name, selected);
                var ammoClone = GetAmmoClone(ammo, ammoKey, getSnapshot, existingNames, ammoClones);
                AddCloneField(ammoClone, field, operation);
                foreach (var weaponName in workspace.References.AmmoWeapons.GetValueOrDefault(ammo.Name) ?? [])
                {
                    var weapon = workspace.Weapon(weaponName)!;
                    var unitTargets = (workspace.References.WeaponUnits.GetValueOrDefault(weaponName) ?? [])
                        .Intersect(selected, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                    if (unitTargets.Length == 0)
                    {
                        continue;
                    }

                    var target = GetWeaponEditTarget(weapon, unitTargets, workspace, units, getSnapshot, existingNames, weaponClones, AddDirect);
                    foreach (var mount in weapon.Mounts.Where(item => item.AmmoName == ammo.Name))
                    {
                        var ammoField = mount.Fields.Single(item => item.Definition.FieldName == "Ammunition");
                        AddWeaponTargetReplacement(target, weapon, ammoField, $"$/GFX/Weapon/{ammoClone.Name}", $"{weapon.Name} 改引 {ammoClone.Name}", AddDirect);
                    }
                }

                continue;
            }

            var sourceWeapon = workspace.Weapon(operation.ObjectName)
                ?? throw new TransactionValidationException($"Weapon 不存在：{operation.ObjectName}");
            var sourceField = sourceWeapon.Field(operation.FieldKey) ?? sourceWeapon.Mounts.SelectMany(item => item.Fields).FirstOrDefault(item => item.Key == operation.FieldKey)
                ?? throw new TransactionValidationException($"Weapon 字段不存在：{operation.FieldKey}");
            if (scope == DraftEditScope.AllReferences)
            {
                AddFieldDirect(sourceField, operation, AddDirect);
            }
            else
            {
                var target = GetWeaponEditTarget(sourceWeapon, selected, workspace, units, getSnapshot, existingNames, weaponClones, AddDirect);
                AddWeaponTargetReplacement(target, sourceWeapon, sourceField, operation.TargetRaw, operation.Summary, AddDirect);
            }
        }

        var newObjects = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var clonesByFile = ammoClones.Values.Concat(weaponClones.Values)
            .GroupBy(item => Normalize(item.Source.RelativeSourceFile), StringComparer.OrdinalIgnoreCase);
        foreach (var group in clonesByFile)
        {
            var snapshot = getSnapshot(group.Key);
            var newline = snapshot.NewLine;
            var blocks = new List<string>();
            foreach (var clone in group.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                var sourceBlock = snapshot.Text.Substring(clone.Source.CharacterOffset, clone.Source.CharacterLength);
                var candidate = SemicolonCsvDocument.ApplyReplacements(sourceBlock, clone.RelativeReplacements);
                blocks.Add(candidate.TrimEnd('\r', '\n'));
                if (!newObjects.TryGetValue(group.Key, out var names))
                {
                    names = [];
                    newObjects[group.Key] = names;
                }

                names.Add(clone.Name);
            }

            var prefix = snapshot.Text.Length == 0 || snapshot.Text.EndsWith(newline, StringComparison.Ordinal) ? newline : newline + newline;
            var append = prefix + string.Join(newline + newline, blocks) + newline;
            AddDirect(group.Key, new TextReplacement(snapshot.Text.Length, 0, string.Empty, append, "追加隔离对象"), $"追加 {blocks.Count} 个隔离对象");
        }

        return new WeaponApplyPlan(
            result,
            newObjects.ToDictionary(item => item.Key, item => (IReadOnlyList<string>)item.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
            [
                $"已解析 {workspace.Weapons.Count:N0} 个 Weapon、{workspace.Ammunition.Count:N0} 个 Ammo 的完整反向影响范围",
                $"局部作用域生成 {ammoClones.Count} 个 Ammo 副本、{weaponClones.Count} 个必要 Weapon 副本",
                "已规划 AmmoBox、MountedWeapon 与 Unit 引用闭包；未新增或删除武器槽"
            ]);
    }

    private static void PlanUnitWeaponReplacement(
        DraftOperation operation,
        UnitWorkspaceData units,
        WeaponWorkspaceData workspace,
        Func<string, TextFileSnapshot> getSnapshot,
        Action<string, TextReplacement, string> add)
    {
        var unit = units.Units.Single(item => item.Name == operation.ObjectName);
        var sourceWeapon = operation.ContextWeaponName ?? NdfSyntaxDocument.Leaf(operation.BaselineRaw);
        var targetWeapon = NdfSyntaxDocument.Leaf(operation.TargetRaw);
        if (workspace.Weapon(targetWeapon) is null)
        {
            throw new TransactionValidationException($"替换目标 Weapon 不存在：{targetWeapon}");
        }

        var snapshot = getSnapshot(unit.Source.RelativeSourceFile);
        var document = new NdfSyntaxDocument(snapshot.Text, unit.Source.CharacterOffset, unit.Source.CharacterLength);
        var references = document.FindReferences("WeaponDescriptor_").Where(item => item.Leaf == sourceWeapon).ToArray();
        if (references.Length == 0)
        {
            throw new TransactionValidationException($"Unit 已不再引用 Weapon：{unit.Name} → {sourceWeapon}");
        }

        foreach (var reference in references)
        {
            var targetRaw = ReplaceLeaf(reference.Raw, targetWeapon);
            add(unit.Source.RelativeSourceFile, new TextReplacement(document.StartOffset(reference.Span), document.Length(reference.Span), reference.Raw, targetRaw, operation.Summary), operation.Summary);
        }
    }

    private static CloneTarget? GetWeaponEditTarget(
        WeaponRecord weapon,
        IReadOnlyList<string> selected,
        WeaponWorkspaceData workspace,
        UnitWorkspaceData units,
        Func<string, TextFileSnapshot> getSnapshot,
        HashSet<string> existingNames,
        IDictionary<string, CloneTarget> clones,
        Action<string, TextReplacement, string> add)
    {
        var referrers = workspace.References.WeaponUnits.GetValueOrDefault(weapon.Name) ?? [];
        if (selected.Count == 0 || selected.Any(item => !referrers.Contains(item, StringComparer.Ordinal)))
        {
            throw new TransactionValidationException($"作用域 Unit 未完整引用 Weapon：{weapon.Name}");
        }

        if (selected.Count == referrers.Count)
        {
            return null;
        }

        var key = IsolationKey(weapon.Name, selected);
        if (!clones.TryGetValue(key, out var clone))
        {
            clone = new CloneTarget(weapon.Source, UniqueName(weapon.Name, existingNames));
            AddDeclarationReplacement(clone, getSnapshot(weapon.Source.RelativeSourceFile));
            clones[key] = clone;
            foreach (var unitName in selected)
            {
                var unit = units.Units.Single(item => item.Name == unitName);
                var snapshot = getSnapshot(unit.Source.RelativeSourceFile);
                var document = new NdfSyntaxDocument(snapshot.Text, unit.Source.CharacterOffset, unit.Source.CharacterLength);
                var references = document.FindReferences("WeaponDescriptor_").Where(item => item.Leaf == weapon.Name).ToArray();
                if (references.Length == 0)
                {
                    throw new TransactionValidationException($"Unit 已不再引用 Weapon：{unitName} → {weapon.Name}");
                }

                foreach (var reference in references)
                {
                    add(unit.Source.RelativeSourceFile,
                        new TextReplacement(document.StartOffset(reference.Span), document.Length(reference.Span), reference.Raw, ReplaceLeaf(reference.Raw, clone.Name), $"{unitName} 改引 {clone.Name}"),
                        $"{unitName} 改引隔离 Weapon {clone.Name}");
                }
            }
        }

        return clone;
    }

    private static CloneTarget GetAmmoClone(
        AmmoRecord ammo,
        string key,
        Func<string, TextFileSnapshot> getSnapshot,
        HashSet<string> existingNames,
        IDictionary<string, CloneTarget> clones)
    {
        if (clones.TryGetValue(key, out var clone))
        {
            return clone;
        }

        clone = new CloneTarget(ammo.Source, UniqueName(ammo.Name, existingNames));
        var snapshot = getSnapshot(ammo.Source.RelativeSourceFile);
        AddDeclarationReplacement(clone, snapshot);
        var sourceBlock = snapshot.Text.Substring(ammo.Source.CharacterOffset, ammo.Source.CharacterLength);
        var guid = Regex.Match(sourceBlock, @"GUID:\{[0-9A-Fa-f-]{36}\}");
        if (!guid.Success)
        {
            throw new TransactionValidationException($"Ammo 缺少可克隆的 DescriptorId GUID：{ammo.Name}");
        }

        clone.RelativeReplacements.Add(new TextReplacement(guid.Index, guid.Length, guid.Value, $"GUID:{{{Guid.NewGuid()}}}", $"{clone.Name} 新 DescriptorId"));
        clones[key] = clone;
        return clone;
    }

    private static void AddDeclarationReplacement(CloneTarget clone, TextFileSnapshot snapshot)
    {
        var block = snapshot.Text.Substring(clone.Source.CharacterOffset, clone.Source.CharacterLength);
        var match = Regex.Match(block, $@"^(\s*(?:export\s+)?){Regex.Escape(clone.Source.Name)}(\s+is\s+)");
        if (!match.Success)
        {
            throw new TransactionValidationException($"无法定位对象声明：{clone.Source.Name}");
        }

        var group = match.Groups[0];
        var relativeNameOffset = match.Groups[1].Length;
        clone.RelativeReplacements.Add(new TextReplacement(relativeNameOffset, clone.Source.Name.Length, clone.Source.Name, clone.Name, $"克隆对象名 {clone.Name}"));
    }

    private static void AddCloneField(CloneTarget clone, WeaponFieldValue field, DraftOperation operation)
    {
        var relativeOffset = field.Location.CharacterOffset - clone.Source.CharacterOffset;
        AddCloneReplacement(clone, new TextReplacement(relativeOffset, field.Location.CharacterLength, operation.BaselineRaw, operation.TargetRaw, operation.Summary));
    }

    private static void AddWeaponTargetReplacement(
        CloneTarget? clone,
        WeaponRecord source,
        WeaponFieldValue field,
        string targetRaw,
        string summary,
        Action<string, TextReplacement, string> add)
    {
        if (clone is null)
        {
            add(field.Location.RelativeSourceFile, new TextReplacement(field.Location.CharacterOffset, field.Location.CharacterLength, field.RawValue, targetRaw, summary), summary);
        }
        else
        {
            AddCloneReplacement(clone, new TextReplacement(field.Location.CharacterOffset - source.Source.CharacterOffset, field.Location.CharacterLength, field.RawValue, targetRaw, summary));
        }
    }

    private static void AddCloneReplacement(CloneTarget clone, TextReplacement replacement)
    {
        var existing = clone.RelativeReplacements.FirstOrDefault(item => item.Offset == replacement.Offset && item.Length == replacement.Length);
        if (existing is null)
        {
            clone.RelativeReplacements.Add(replacement);
        }
        else if (!string.Equals(existing.Target, replacement.Target, StringComparison.Ordinal))
        {
            throw new TransactionValidationException($"隔离副本字段目标冲突：{replacement.Description}");
        }
    }

    private static void AddFieldDirect(WeaponFieldValue field, DraftOperation operation, Action<string, TextReplacement, string> add) =>
        add(field.Location.RelativeSourceFile, new TextReplacement(field.Location.CharacterOffset, field.Location.CharacterLength, operation.BaselineRaw, operation.TargetRaw, operation.Summary), operation.Summary);

    private static bool IsP4(DraftOperation operation) => operation.TargetKind is
        DraftTargetKind.WeaponField or DraftTargetKind.MountedWeaponAmmo or DraftTargetKind.AmmoField or DraftTargetKind.UnitWeaponReference;

    private static string IsolationKey(string objectName, IReadOnlyList<string> selected) =>
        objectName + "|" + string.Join("|", selected.Order(StringComparer.Ordinal));

    private static string UniqueName(string source, ISet<string> existing)
    {
        while (true)
        {
            var name = $"{source}_WLMT_{Guid.NewGuid():N}"[..Math.Min(source.Length + 14, 120)];
            if (existing.Add(name))
            {
                return name;
            }
        }
    }

    private static string ReplaceLeaf(string raw, string targetLeaf)
    {
        var slash = raw.LastIndexOf('/');
        return slash >= 0 ? raw[..(slash + 1)] + targetLeaf : targetLeaf;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
