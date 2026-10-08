using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarnoLiteModdingTool.App.ViewModels;

namespace WarnoLiteModdingTool.App.SourceWorkspace;

internal static class SourceOrigin
{
    public static Action Capture(MainViewModel main, FrameworkElement origin)
    {
        var module = main.SelectedModule?.Key;
        var unit = main.UnitWorkspace; var selectedUnit = unit?.SelectedUnit;
        var weapon = main.WeaponWorkspace; var weaponUnit = weapon?.SelectedUnit; var selectedWeapon = weapon?.SelectedWeapon; var mount = weapon?.SelectedMount;
        var ammo = main.AmmoWorkspace; var selectedAmmo = ammo?.SelectedAmmo;
        var division = main.DivisionWorkspace; var selectedDivision = division?.SelectedDivision; var rule = division?.SelectedRule; var pack = division?.SelectedPack;
        var strategic = main.StrategicWorkspace; var selectedStrategic = strategic?.Selected; var selectedPack = strategic?.Packs.Selected;
        var unitText = unit?.TextFilter; var unitMode = unit?.FilterMatchMode;
        var filters = unit?.FilterDimensions.SelectMany(d => d.Options).Select(o => (Option: o, Selected: o.IsSelected)).ToArray();
        var checks = unit?.Units.Select(u => (Unit: u, Selected: u.IsBatchSelected)).ToArray();
        var sections = unit?.FieldSections.Select(s => (s.Title, s.IsExpanded)).ToArray();
        var weaponSearch = weapon?.UnitSearchText; var weaponScope = weapon?.SelectedScope;
        var ammoSearch = ammo?.SearchText; var divisionSearch = division?.Search; var strategicSearch = strategic?.Search;
        var scrolls = new List<(ScrollViewer View, double X, double Y)>();
        DependencyObject? node = origin;
        while (node is not null) { if (node is ScrollViewer scroll) scrolls.Add((scroll, scroll.HorizontalOffset, scroll.VerticalOffset)); node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node); }
        return () =>
        {
            if (module == "units" && ReferenceEquals(unit, main.UnitWorkspace) && unit is not null)
            {
                unit.TextFilter = unitText!; unit.FilterMatchMode = unitMode!;
                foreach (var (option, selected) in filters!) option.IsSelected = selected;
                foreach (var (item, selected) in checks!) item.IsBatchSelected = selected;
                unit.SelectedUnit = selectedUnit;
                foreach (var section in unit.FieldSections) if (sections!.FirstOrDefault(s => s.Title == section.Title) is var prior) section.IsExpanded = prior.IsExpanded;
            }
            if (module == "weapons" && ReferenceEquals(weapon, main.WeaponWorkspace) && weapon is not null) { weapon.UnitSearchText = weaponSearch!; weapon.SelectedScope = weaponScope!; weapon.SelectedUnit = weaponUnit; weapon.SelectedWeapon = selectedWeapon; weapon.SelectedMount = mount; }
            if (module == "ammo" && ReferenceEquals(ammo, main.AmmoWorkspace) && ammo is not null) { ammo.SearchText = ammoSearch!; ammo.SelectedAmmo = selectedAmmo; }
            if (module == "divisions" && ReferenceEquals(division, main.DivisionWorkspace) && division is not null) { division.Search = divisionSearch!; division.SelectedDivision = selectedDivision; division.SelectedRule = rule; division.SelectedPack = pack; }
            if (ReferenceEquals(strategic, main.StrategicWorkspace) && strategic is not null) { if (module == "strategic") { strategic.Search = strategicSearch!; strategic.Selected = selectedStrategic; } if (module == "sp") strategic.Packs.Selected = selectedPack; }
            origin.Dispatcher.BeginInvoke(new Action(() => { foreach (var (view, x, y) in scrolls) { view.ScrollToHorizontalOffset(x); view.ScrollToVerticalOffset(y); } }));
        };
    }
}
