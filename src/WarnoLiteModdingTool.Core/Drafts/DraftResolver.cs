using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Strategic;

namespace WarnoLiteModdingTool.Core.Drafts;

public static class DraftResolver
{
    public static IReadOnlyList<ResolvedDraftOperation> Resolve(
        UnitWorkspaceData workspace,
        IEnumerable<DraftOperation> operations) => Resolve(workspace, null, operations);

    public static IReadOnlyList<ResolvedDraftOperation> Resolve(
        UnitWorkspaceData workspace,
        WeaponWorkspaceData? weapons,
        IEnumerable<DraftOperation> operations) => Resolve(workspace, weapons, null, operations);

    public static IReadOnlyList<ResolvedDraftOperation> Resolve(
        UnitWorkspaceData workspace,
        WeaponWorkspaceData? weapons,
        DivisionWorkspaceData? divisions,
        IEnumerable<DraftOperation> operations,
        StrategicWorkspace? strategic = null)
    {
        var units = workspace.Units.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var result = new List<ResolvedDraftOperation>();
        foreach (var operation in operations)
        {
            if (operation.TargetKind == DraftTargetKind.GlobalRule) { result.Add(workspace.Rules?.Resolve(operation) ?? new(operation, DraftResolutionStatus.Conflict, "游戏规则模块未加载")); continue; }
            if(operation.TargetKind==DraftTargetKind.AmmoName){result.Add(AmmoNames.Resolve(workspace,weapons,operation));continue;}
            if(operation.TargetKind==DraftTargetKind.DivisionIdentity){result.Add(DivisionIdentity.Resolve(divisions,operation));continue;}
            if(operation.TargetKind==DraftTargetKind.UnitCreate){result.Add(UnitCreation.Resolve(workspace,operation));continue;}
            if (operation.TargetKind == DraftTargetKind.StrategicPlan)
            {
                result.Add(StrategicPlanner.Resolve(strategic, operation));
                continue;
            }
            if (operation.TargetKind == DraftTargetKind.DivisionPlan)
            {
                result.Add(ResolveDivisionOperation(divisions, operation));
                continue;
            }

            if (operation.TargetKind is DraftTargetKind.WeaponField or DraftTargetKind.MountedWeaponAmmo or DraftTargetKind.AmmoField)
            {
                result.Add(ResolveWeaponOperation(workspace, weapons, operation));
                continue;
            }

            if (!units.TryGetValue(operation.ObjectName, out var unit))
            {
                result.Add(Conflict(operation, "目标 Unit 已不存在"));
                continue;
            }

            if (!string.Equals(unit.Source.TypeName, operation.ObjectType, StringComparison.Ordinal))
            {
                result.Add(Conflict(operation, "目标对象类型已变化"));
                continue;
            }

            if (operation.TargetKind == DraftTargetKind.UnitWeaponReference)
            {
                var sourceWeapon = operation.ContextWeaponName ?? NdfSyntaxDocument.Leaf(operation.BaselineRaw);
                if (!unit.Weapons.Contains(sourceWeapon, StringComparer.Ordinal))
                {
                    result.Add(Conflict(operation, "Unit 的 Weapon 引用基线已变化"));
                }
                else if (weapons is null || weapons.Weapon(NdfSyntaxDocument.Leaf(operation.TargetRaw)) is null)
                {
                    result.Add(Conflict(operation, "目标 Weapon 不存在"));
                }
                else
                {
                    result.Add(Active(operation));
                }

                continue;
            }

            if (operation.TargetKind == DraftTargetKind.UnitName)
            {
                if (operation.BaselineNameToken is null)
                {
                    result.Add(Conflict(operation, "旧版名称草稿缺少 NameToken 基线；请重新编辑该名称"));
                }
                else if (!string.Equals(unit.NameToken, operation.BaselineNameToken, StringComparison.Ordinal))
                {
                    result.Add(Conflict(operation, "NameToken 基线已变化"));
                }
                else if (!string.Equals(unit.DisplayName, operation.BaselineValue, StringComparison.Ordinal))
                {
                    result.Add(Conflict(operation, "游戏内名称基线已变化"));
                }
                else if (!unit.CanEditName)
                {
                    result.Add(Conflict(operation, unit.NameEditReason));
                }
                else if (!string.Equals(
                             Normalize(unit.UnitsCsvRelativePath ?? string.Empty),
                             Normalize(operation.RelativeSourceFile),
                             StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(Conflict(operation, "UNITS.csv 目标已变化"));
                }
                else
                {
                    result.Add(Active(operation));
                }

                continue;
            }

            if (operation.TargetKind == DraftTargetKind.OptionalUnitModule)
            {
                var optionalField = unit.Field(operation.FieldKey);
                if (optionalField is null || !optionalField.Definition.CanInsertWhenMissing)
                {
                    result.Add(Conflict(operation, "字段不允许补建结构"));
                }
                else if (optionalField.Availability != UnitFieldAvailability.Missing)
                {
                    result.Add(Conflict(operation, "目标结构已经存在，请重新编辑当前字段"));
                }
                else if (!string.Equals(Normalize(unit.Source.RelativeSourceFile), Normalize(operation.RelativeSourceFile), StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(Conflict(operation, "Unit 源文件已变化"));
                }
                else if (!UnitValueConverter.TryFormatTarget(optionalField, operation.TargetValue, out var normalized, out var raw, out _) ||
                         !string.Equals(normalized, operation.TargetValue, StringComparison.Ordinal) ||
                         !string.Equals(raw, operation.TargetRaw, StringComparison.Ordinal))
                {
                    result.Add(Conflict(operation, "补建字段的目标值无效"));
                }
                else
                {
                    result.Add(Active(operation));
                }

                continue;
            }

            var field = unit.Field(operation.FieldKey);
            if (field is null)
            {
                result.Add(Conflict(operation, "字段定义已不存在"));
            }
            else if (!string.Equals(
                         Normalize(field.Location?.RelativeSourceFile ?? string.Empty),
                         Normalize(operation.RelativeSourceFile),
                         StringComparison.OrdinalIgnoreCase))
            {
                result.Add(Conflict(operation, "字段源文件已变化"));
            }
            else if (!field.CanEdit)
            {
                result.Add(Conflict(operation, field.Reason));
            }
            else if (!string.Equals(field.RawValue, operation.BaselineRaw, StringComparison.Ordinal))
            {
                result.Add(Conflict(operation, "字段原值已变化"));
            }
            else
            {
                result.Add(Active(operation));
            }
        }

        return result;
    }

    private static ResolvedDraftOperation ResolveDivisionOperation(
        DivisionWorkspaceData? workspace,
        DraftOperation operation)
    {
        if (workspace is null)
        {
            return Conflict(operation, "战术师工作区不可用");
        }

        var division = workspace.Division(operation.ObjectName);
        if (division is null || !string.Equals(division.Source.TypeName, operation.ObjectType, StringComparison.Ordinal))
        {
            return Conflict(operation, "目标师已不存在或类型已变化");
        }

        if (!string.Equals(Normalize(division.Source.RelativeSourceFile), Normalize(operation.RelativeSourceFile), StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(operation, "目标师源文件已变化");
        }

        if (!DivisionDraftCodec.TryDeserialize(operation.BaselineRaw, out var baseline, out _))
        {
            return Conflict(operation, "战术师草稿缺少可解析的完整基线");
        }

        if (!string.Equals(DivisionDraftCodec.Serialize(division.Baseline), DivisionDraftCodec.Serialize(baseline), StringComparison.Ordinal))
        {
            return Conflict(operation, "师、单位池、默认 Deck 或费用曲线基线已变化");
        }

        if (!DivisionDraftCodec.TryDeserialize(operation.TargetRaw, out var target, out var error))
        {
            return Conflict(operation, $"战术师目标无法解析：{error}");
        }

        if (!target.DefaultDeck.SequenceEqual(baseline.DefaultDeck))
        {
            return Conflict(operation, "默认卡组编辑已移除；请重新打开该师并保存其他改动");
        }

        var errors = DivisionStateValidator.Validate(workspace, division, target);
        return errors.Count == 0
            ? Active(operation)
            : Conflict(operation, string.Join("；", errors.Take(3)));
    }

    private static ResolvedDraftOperation ResolveWeaponOperation(
        UnitWorkspaceData units,
        WeaponWorkspaceData? workspace,
        DraftOperation operation)
    {
        if (workspace is null)
        {
            return Conflict(operation, "Weapon/Ammo 工作区不可用");
        }

        WeaponFieldValue? field;
        if (operation.TargetKind == DraftTargetKind.AmmoField)
        {
            var ammo = workspace.Ammo(operation.ObjectName);
            if (ammo is null || !string.Equals(ammo.Source.TypeName, operation.ObjectType, StringComparison.Ordinal))
            {
                return Conflict(operation, "目标 Ammo 已不存在或类型已变化");
            }

            field = ammo.Field(operation.FieldKey);
        }
        else
        {
            var weapon = workspace.Weapon(operation.ObjectName);
            if (weapon is null || !string.Equals(weapon.Source.TypeName, operation.ObjectType, StringComparison.Ordinal))
            {
                return Conflict(operation, "目标 Weapon 已不存在或类型已变化");
            }

            field = weapon.Field(operation.FieldKey) ?? weapon.Mounts.SelectMany(item => item.Fields).FirstOrDefault(item => item.Key == operation.FieldKey);
        }

        if (field is null)
        {
            return Conflict(operation, "字段已不存在或不再可唯一定位");
        }

        if (!string.Equals(Normalize(field.Location.RelativeSourceFile), Normalize(operation.RelativeSourceFile), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(field.RawValue, operation.BaselineRaw, StringComparison.Ordinal))
        {
            return Conflict(operation, "字段源位置或原值已变化");
        }

        if (!WeaponValueConverter.TryFormat(field, operation.TargetValue, out var normalized, out var raw, out _) ||
            !string.Equals(normalized, operation.TargetValue, StringComparison.Ordinal) ||
            !string.Equals(raw, operation.TargetRaw, StringComparison.Ordinal))
        {
            return Conflict(operation, "草稿目标值与当前字段规则不一致");
        }

        var scope = operation.EditScope ?? DraftEditScope.CurrentUnit;
        if (scope != DraftEditScope.AllReferences)
        {
            var selected = operation.SelectedUnitNames?.Distinct(StringComparer.Ordinal).ToArray() ?? [];
            if (selected.Length == 0 || selected.Any(name => units.Units.All(unit => unit.Name != name)))
            {
                return Conflict(operation, "作用域中的 Unit 不存在");
            }

            var affected = operation.TargetKind == DraftTargetKind.AmmoField
                ? workspace.References.AmmoUnits.GetValueOrDefault(operation.ObjectName) ?? []
                : workspace.References.WeaponUnits.GetValueOrDefault(operation.ObjectName) ?? [];
            if (selected.Any(name => !affected.Contains(name, StringComparer.Ordinal)))
            {
                return Conflict(operation, "所选 Unit 已不再引用目标对象");
            }
        }

        return Active(operation);
    }

    private static ResolvedDraftOperation Active(DraftOperation operation) =>
        new(operation, DraftResolutionStatus.Active, string.Empty);

    private static ResolvedDraftOperation Conflict(DraftOperation operation, string reason) =>
        new(operation, DraftResolutionStatus.Conflict, reason);

    private static string Normalize(string path) => path.Replace('\\', '/');
}
