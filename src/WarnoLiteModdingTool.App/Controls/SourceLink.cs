using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.SourceWorkspace;
using WarnoLiteModdingTool.App.ViewModels;

namespace WarnoLiteModdingTool.App.Controls;

/// <summary>Shared professional-mode entry, including detached business panels.</summary>
public sealed class SourceLink : Button
{
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(nameof(Caption), typeof(string), typeof(SourceLink), new PropertyMetadata("在工作台中查看", (o, e) => UiText.Bind(o, ContentProperty, (string)e.NewValue)));
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(nameof(Compact), typeof(bool), typeof(SourceLink), new PropertyMetadata(false, (o, e) =>
    { var link = (SourceLink)o; link.Caption = (bool)e.NewValue ? "</>" : "在工作台中查看"; link.SetResourceReference(StyleProperty, (bool)e.NewValue ? "SourceLinkCompactButton" : "SourceLinkButton"); }));
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(nameof(Target), typeof(object), typeof(SourceLink), new PropertyMetadata(null, (o, e) => ((SourceLink)o).IsEnabled = e.NewValue is not null));
    public object? Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public SourceLink()
    {
        SetResourceReference(StyleProperty, "SourceLinkButton");
        UiText.Bind(this, ContentProperty, Caption);
        UiText.Bind(this, ToolTipProperty, "查看当前对象或参数的源码；不自动应用草稿");
        HorizontalAlignment = HorizontalAlignment.Left;
        Visibility = Visibility.Collapsed;
        IsEnabled = false;
        Loaded += (_, _) => { if (Host(this)?.DataContext is MainViewModel main) SetBinding(VisibilityProperty, new Binding(nameof(main.AdvancedMode)) { Source = main, Converter = new BooleanToVisibilityConverter() }); };
        Click += async (_, e) => { e.Handled = true; await OpenAsync(this, Target); };
    }

    public static MainWindow? Host(DependencyObject element)
    {
        var window = Window.GetWindow(element);
        while (window is not null) { if (window is MainWindow main) return main; window = window.Owner; }
        return null;
    }

    public static async Task OpenAsync(FrameworkElement origin, object? item)
    {
        var host = Host(origin);
        if (host?.DataContext is not MainViewModel main || !main.AdvancedMode) return;
        try
        {
            var targets = BusinessSourceTargets.Create(main, item);
            if (targets.Count == 0) throw new InvalidOperationException(UiText.T("无直接源码位置；新建内容需先应用，或查看所属对象。"));
            var target = targets.Count == 1 ? targets[0] : Pick(host, targets, t => t.Label + "\n" + t.Path);
            if (target is not null) await host.OpenBusinessSourceAsync(target, origin);
        }
        catch (Exception ex) { LocalizedMessageBox.Show(Window.GetWindow(origin) ?? host, ex.Message, UiText.T("源码定位")); }
    }

    internal static T? Pick<T>(Window owner, IReadOnlyList<T> items, Func<T, string> label) where T : class
    {
        var window = new Window { Owner = owner, Title = UiText.T("选择源码位置"), Width = 720, Height = 410, MinWidth = 420, MinHeight = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(BackgroundProperty, "SurfaceBrush"); window.SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new DockPanel { Margin = new Thickness(14) }; window.Content = panel;
        var open = new Button { Content = UiText.T("在工作台中查看"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), IsDefault = true };
        open.SetResourceReference(StyleProperty, "SecondaryButton"); DockPanel.SetDock(open, Dock.Bottom); panel.Children.Add(open);
        var list = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        foreach (var item in items) list.Items.Add(new ListBoxItem { Tag = item, Content = new TextBlock { Text = label(item), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) } });
        panel.Children.Add(list); T? selected = null;
        void Choose() { if (list.SelectedItem is ListBoxItem { Tag: T item }) { selected = item; window.DialogResult = true; } }
        open.Click += (_, _) => Choose(); list.MouseDoubleClick += (_, _) => Choose();
        window.ShowDialog(); return selected;
    }

    public static readonly DependencyProperty RowMenuProperty = DependencyProperty.RegisterAttached("RowMenu", typeof(bool), typeof(SourceLink), new PropertyMetadata(false, (o, e) =>
    {
        if (o is FrameworkElement element && e.NewValue is true) { element.ContextMenu ??= new ContextMenu(); element.ContextMenuOpening -= Menu; element.ContextMenuOpening += Menu; }
    }));
    public static bool GetRowMenu(DependencyObject o) => (bool)o.GetValue(RowMenuProperty);
    public static void SetRowMenu(DependencyObject o, bool value) => o.SetValue(RowMenuProperty, value);
    private static void Menu(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement root) return;
        if (Host(root)?.DataContext is not MainViewModel { AdvancedMode: true }) { e.Handled = true; return; }
        DependencyObject? node = e.OriginalSource as DependencyObject; FrameworkElement? row = null;
        while (node is not null && node != root) { if (node is DataGridRow or ListBoxItem or TreeViewItem) { row = (FrameworkElement)node; break; } node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node); }
        if (row is null) { e.Handled = true; return; }
        var target = row.DataContext;
        var menu = new ContextMenu(); var entry = new MenuItem { Header = UiText.T("在工作台中查看") };
        entry.Click += async (_, _) => await OpenAsync(root, target); menu.Items.Add(entry); root.ContextMenu = menu; menu.PlacementTarget = row; menu.IsOpen = true; e.Handled = true;
    }
}
