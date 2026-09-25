using System.ComponentModel;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.App.ViewModels;

// Navigation is independent of source offsets and may be restored after replacing a data generation.
internal sealed class WorkspaceNavigation(Func<MainViewModel, string?> restore)
{
    public string? Restore(MainViewModel main) => restore(main);
    public static WorkspaceNavigation Capture(MainViewModel main, ApplyPreview preview)
    {
        var module = main.SelectedModule?.Key;
        var unit = main.UnitWorkspace; var weapon = main.WeaponWorkspace; var ammo = main.AmmoWorkspace;
        var division = main.DivisionWorkspace; var strategic = main.StrategicWorkspace; var rules = main.RulesWorkspace;
        var unitSelection = unit?.SelectedUnit?.InternalName;
        var rename = preview.Operations.FirstOrDefault(o => o.TargetKind == DraftTargetKind.UnitRename && o.ObjectName == unitSelection);
        if (rename is not null) unitSelection = UnitIdentityEditing.Read(rename).NewName;
        var orderedUnits = unit?.UnitsView.Cast<Units.UnitListItemViewModel>().Select(u => u.InternalName).ToArray() ?? [];
        var selectedIndex = Array.IndexOf(orderedUnits, unit?.SelectedUnit?.InternalName);
        var neighbours = orderedUnits.Skip(Math.Max(0, selectedIndex + 1)).Concat(orderedUnits.Take(Math.Max(0, selectedIndex)).Reverse()).ToArray();
        var unitChecks = unit?.Units.Where(u => u.IsBatchSelected).Select(u => u.InternalName).ToHashSet() ?? [];
        var unitFilters = unit?.FilterDimensions.SelectMany(d => d.Options).Where(o => o.IsSelected).Select(o => (o.DimensionKey, o.Value)).ToHashSet() ?? [];
        var weaponChecks = weapon?.Units.Where(u => u.IsWeaponScopeSelected).Select(u => u.InternalName).ToHashSet() ?? [];
        var ammoChecks = ammo?.Ammunition.Where(a => a.IsBatchSelected).Select(a => a.Name).ToHashSet() ?? [];
        var sectionStates = unit?.FieldSections.ToDictionary(s => s.Title, s => s.IsExpanded) ?? [];
        var draftChecks = unit?.DraftItems.Where(d => d.IsSelected).Select(d => d.Resolved.Operation.Id).ToHashSet() ?? [];
        return new(m =>
        {
            string? notice = null;
            if (unit is not null && m.UnitWorkspace is { } u)
            {
                u.TextFilter = unit.TextFilter; u.FilterMatchMode = unit.FilterMatchMode;
                foreach (var o in u.FilterDimensions.SelectMany(d => d.Options)) o.SetSelected(unitFilters.Contains((o.DimensionKey, o.Value)), false);
                u.RefreshExternalDraftState();
                u.SelectedUnit = u.Units.FirstOrDefault(x => x.InternalName == unitSelection);
                if (u.SelectedUnit is null && unitSelection is not null)
                {
                    var available = u.Units.ToDictionary(x => x.InternalName, StringComparer.Ordinal);
                    u.SelectedUnit = neighbours.Select(n => available.GetValueOrDefault(n)).FirstOrDefault(x => x is not null) ?? u.Units.FirstOrDefault();
                    if (module == "units") notice = "应用完成；原选中单位已移除，已选择相邻单位";
                }
                u.RestoreBatchChecks(unitChecks); u.IsBatchToolsOpen = unit.IsBatchToolsOpen;
                foreach (var section in u.FieldSections) section.IsExpanded = sectionStates.GetValueOrDefault(section.Title);
                foreach (var draft in u.DraftItems) draft.IsSelected = draftChecks.Contains(draft.Resolved.Operation.Id);
                CopySort(unit.UnitsView, u.UnitsView);
            }
            if (weapon is not null && m.WeaponWorkspace is { } w)
            {
                w.UnitSearchText = weapon.UnitSearchText; w.UnitFilter = weapon.UnitFilter;
                w.SelectedUnit = w.Units.FirstOrDefault(u => u.InternalName == weapon.SelectedUnit?.InternalName) ?? w.Units.FirstOrDefault();
                foreach (var item in w.Units) item.IsWeaponScopeSelected = weaponChecks.Contains(item.InternalName);
                w.SelectedScope = weapon.SelectedScope;
                w.SelectedWeapon = w.Weapons.FirstOrDefault(x => x.Name == weapon.SelectedWeapon?.Name) ?? w.Weapons.FirstOrDefault();
                w.SelectedMount = w.Mounts.FirstOrDefault(x => x.Mount.Index == weapon.SelectedMount?.Mount.Index) ?? w.Mounts.FirstOrDefault();
                w.UnitsView.Refresh(); CopySort(weapon.UnitsView, w.UnitsView);
            }
            if (ammo is not null && m.AmmoWorkspace is { } a)
            {
                a.SearchText = ammo.SearchText; a.AmmoFilter = ammo.AmmoFilter; a.ReferenceSearchText = ammo.ReferenceSearchText;
                a.SelectedAmmo = a.Ammunition.FirstOrDefault(x => x.Name == ammo.SelectedAmmo?.Name) ?? a.Ammunition.FirstOrDefault();
                a.RestoreBatchChecks(ammoChecks); a.BatchOpen = ammo.BatchOpen; a.RefreshFilter(); CopySort(ammo.AmmunitionView, a.AmmunitionView);
            }
            if (division is not null && m.DivisionWorkspace is { } d)
            {
                d.Search = division.Search; d.DivisionFilter = division.DivisionFilter; d.RuleFilter = division.RuleFilter;
                d.SelectedDivision = d.Divisions.FirstOrDefault(x => x.InternalName == division.SelectedDivision?.InternalName) ?? d.Divisions.FirstOrDefault();
                d.DivisionsView.Refresh(); CopySort(division.DivisionsView, d.DivisionsView);
            }
            if (strategic is not null && m.StrategicWorkspace is { } s)
            {
                s.Search = strategic.Search; s.ListFilter = strategic.ListFilter;
                s.Selected = s.Data.Records.FirstOrDefault(x => x.Id == strategic.Selected?.Id) ?? s.Data.Records.FirstOrDefault();
                s.Packs.Selected = s.Packs.Items.FirstOrDefault(x => x.Id == strategic.Packs.Selected?.Id) ?? s.Packs.Items.FirstOrDefault();
                s.RefreshList(); CopySort(strategic.Packs.View, s.Packs.View);
            }
            if (rules is not null && m.RulesWorkspace is { } r) { r.Category = rules.Category; r.Search = rules.Search; r.Refresh(); }
            m.SelectedModule = m.Modules.FirstOrDefault(x => x.Key == module) ?? m.Modules.FirstOrDefault();
            return notice;
        });
    }
    private static void CopySort(ICollectionView oldView, ICollectionView newView)
    {
        if (!newView.CanSort) return;
        using (newView.DeferRefresh()) { newView.SortDescriptions.Clear(); foreach (var sort in oldView.SortDescriptions) newView.SortDescriptions.Add(sort); }
    }
}
