using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.Controls;

public sealed class WeaponSlotAddWindow : Window
{
    public WeaponStructureState? Result { get; private set; }
    private sealed record Choice(string Id, string Label, string Search);
    public WeaponSlotAddWindow(WeaponWorkspaceData data, WeaponStructureState state, UnitProjectGraph graph, string? copyId = null)
    {
        Title = UiText.T("添加武器槽"); Width = 800; Height = 720; MinWidth = 640; MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var panel = new StackPanel(); root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) }; error.SetResourceReference(ForegroundProperty, "ErrorBrush");
        void Row(string label, FrameworkElement control, string parameter = "") => panel.Children.Add(new FieldRow { Header = UiText.T(label), Parameter = parameter, Content = control, Margin = new Thickness(0, 0, 0, 10) });
        var units = new SearchPicker { ItemsSource = data.Units, DisplayMemberPath = "DisplayName", SecondaryMemberPath = "Name" };
        var weapons = new SearchPicker { DisplayMemberPath = "Name" };
        var mounts = new SearchPicker { DisplayMemberPath = "Label", SecondaryMemberPath = "Search" };
        var target = new WeaponStructureSyntax(state.Weapon.Body);
        var copiedRow = copyId is null ? null : WeaponStructure.Rows(state).SingleOrDefault(r => r.Id == copyId && !r.Removed);
        var copiedAddition = state.Added.SingleOrDefault(a => a.Id == copyId);
        var sourceName = copiedAddition?.Source.Name ?? state.Weapon.Name;
        var sourceMountId = copiedAddition?.SourceMount ?? copyId;
        var turrets = new ComboBox { ItemsSource = target.Turrets.Select((t, i) => new Choice(t.Id, UiText.T("炮塔") + " " + (i + 1) + " · " + t.Type, t.Type)).ToArray(), DisplayMemberPath = "Label", SelectedIndex = 0 };
        var visual = new SearchPicker { DisplayMemberPath = "Label", SecondaryMemberPath = "Search" };
        var ammo = new SearchPicker { ItemsSource = data.Ammunition, DisplayMemberPath = "DisplayName", SecondaryMemberPath = "Name" };
        var mode = new ComboBox { ItemsSource = new[] { UiText.T("独立弹药箱"), UiText.T("共用已有弹药箱") }, SelectedIndex = 0 };
        var boxes = new ComboBox { ItemsSource = state.Boxes.Select((p, i) => new Choice(p.Key, UiText.T("弹药箱") + " " + (i + 1) + " · " + p.Value + " · " + string.Join(", ", WeaponStructure.Rows(state).Where(r => !r.Removed && r.Box == p.Key).Select(r => data.Ammo(r.Ammo)?.DisplayName ?? r.Ammo)), p.Key)).ToArray(), DisplayMemberPath = "Label", SelectedIndex = -1, IsEnabled = false };
        var salves = new TextBox { Text = "" };
        var copyTurret = new CheckBox { Content = UiText.T("复制来源炮塔参数，复用目标现有挂点"), Margin = new Thickness(0, 8, 0, 8) };
        Row("来源单位", units); Row("来源武器配置", weapons); Row("参考挂载", mounts); Row("目标炮塔", turrets);
        Row("目标表现挂载", visual); Row("使用的Ammo", ammo, "Ammunition"); Row("库存方式", mode); Row("共用弹药箱", boxes); Row("独立箱齐射次数", salves, "Salves");
        panel.Children.Add(copyTurret);
        panel.Children.Add(new TextBlock { Text = UiText.T("新增挂载使用目标已有开火位置；可适配的表现操作器将独立复制。不会创建新的模型或骨骼。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(error);
        void Targets()
        {
            var tid = (turrets.SelectedItem as Choice)?.Id;
            visual.ItemsSource = target.Mounts.Where(m => m.TurretId == tid).Select(m => new Choice(m.Id, (data.Ammo(NdfSyntaxDocument.Leaf(m.Ammo))?.DisplayName ?? m.Ammo) + " · " + m.Id, m.Ammo)).ToArray();
            visual.SelectedItem = (visual.ItemsSource as Choice[])?.FirstOrDefault();
        }
        turrets.SelectionChanged += (_, _) => Targets(); Targets();
        mode.SelectionChanged += (_, _) => { boxes.IsEnabled = mode.SelectedIndex == 1; salves.IsEnabled = mode.SelectedIndex == 0; };
        units.SelectedItemChanged += (_, _) => { weapons.ItemsSource = ((units.SelectedItem as UnitRecord)?.Weapons ?? []).Select(data.Weapon).Where(w => w is not null).ToArray(); weapons.SelectedItem = ((weapons.ItemsSource as WeaponRecord[]) ?? []).FirstOrDefault(); };
        weapons.SelectedItemChanged += (_, _) =>
        {
            try
            {
                if (weapons.SelectedItem is not WeaponRecord w) return;
                var syntax = new WeaponStructureSyntax(WeaponStructure.Source(w).Body);
                mounts.ItemsSource = syntax.Mounts.Select(m => new Choice(m.Id, (data.Ammo(NdfSyntaxDocument.Leaf(m.Ammo))?.DisplayName ?? m.Ammo) + " · " + m.Id, m.Ammo)).ToArray();
                mounts.SelectedItem = w.Name == sourceName && sourceMountId is not null ? (mounts.ItemsSource as Choice[])?.FirstOrDefault(c => c.Id == sourceMountId) : null;
            }
            catch (Exception e) { error.Text = UiText.T(e.Message); mounts.ItemsSource = Array.Empty<Choice>(); }
        };
        mounts.SelectedItemChanged += (_, _) =>
        {
            if (weapons.SelectedItem is not WeaponRecord w || mounts.SelectedItem is not Choice c) return;
            var syntax = new WeaponStructureSyntax(WeaponStructure.Source(w).Body); var m = syntax.Mounts.Single(m => m.Id == c.Id);
            ammo.SelectedItem = data.Ammo(NdfSyntaxDocument.Leaf(m.Ammo)); salves.Text = syntax.Document.Raw(syntax.Boxes[m.Box]);
        };
        Button Action(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = UiText.T(text), Margin = new Thickness(8, 14, 0, 0), MinHeight = 32 };
            b.SetResourceReference(StyleProperty, "SecondaryButton"); b.Click += click; footer.Children.Add(b); return b;
        }
        Action("取消", (_, _) => Close());
        Action("加入草稿", (_, _) =>
        {
            try
            {
                if (units.SelectedItem is not UnitRecord u || weapons.SelectedItem is not WeaponRecord w || mounts.SelectedItem is not Choice m ||
                    turrets.SelectedItem is not Choice t || visual.SelectedItem is not Choice v || ammo.SelectedItem is not AmmoRecord a) throw new InvalidOperationException("请选择来源、目标炮塔、表现挂载和弹药");
                var count = mode.SelectedIndex == 0 ? int.Parse(salves.Text, NumberStyles.Integer, CultureInfo.InvariantCulture) : 0;
                var candidate = WeaponStructure.Copy(state); var originalAmmo = graph.RequireObject(a.Source.RelativeSourceFile, a.Name);
                var raw = graph.ReferenceTo(state.Weapon.File, originalAmmo);
                if (mode.SelectedIndex == 1 && boxes.SelectedItem is not Choice) throw new InvalidOperationException("请选择共用弹药箱");
                var newId = WeaponStructure.Add(candidate, WeaponStructure.Source(w), m.Id, u.Name, t.Id, v.Id, raw, count, mode.SelectedIndex == 1 ? (boxes.SelectedItem as Choice)?.Id : null, copyTurret.IsChecked == true);
                if (copiedRow is not null && w.Name == sourceName && m.Id == sourceMountId)
                {
                    var values = WeaponStructureSyntax.Values(copiedRow.Body, "TMountedWeaponDescriptor", ["NbWeapons", "HideInInterface"]);
                    if (values.Count > 0) candidate.Fields[newId] = values;
                    if (a.Name == copiedRow.Ammo && state.AmmoFields.TryGetValue(copiedRow.Id, out var edits)) candidate.AmmoFields[newId] = new(edits);
                }
                WeaponStructure.Validate(candidate, data); WeaponStructurePresentation.Capture(graph, candidate); WeaponStructurePresentation.Validate(graph, candidate);
                _ = WeaponStructureRenderer.Render(candidate, candidate.Weapon.Body);
                Result = candidate; DialogResult = true;
            }
            catch (Exception e) when (e is InvalidOperationException or System.IO.IOException or ArgumentException or FormatException or OverflowException) { error.Text = UiText.T(e.Message); }
        });
        units.SelectedItem = data.Units.FirstOrDefault(u => u.Name == copiedAddition?.SourceUnit) ?? data.Units.FirstOrDefault(u => u.Name == state.Unit && u.Weapons.Count > 0) ?? data.Units.FirstOrDefault(u => u.Weapons.Contains(state.Weapon.Name));
        if (weapons.ItemsSource is IEnumerable<WeaponRecord> ws) weapons.SelectedItem = ws.FirstOrDefault(w => w.Name == sourceName) ?? ws.FirstOrDefault();
        if (copiedRow is not null) { ammo.SelectedItem = data.Ammo(copiedRow.Ammo); salves.Text = state.Boxes[copiedRow.Box].ToString(CultureInfo.InvariantCulture); }
    }
}
