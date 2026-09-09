using MessageBox = WarnoLiteModdingTool.App.Localisation.LocalizedMessageBox;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Theming;

namespace WarnoLiteModdingTool.App.Settings;

public sealed class SettingsWindow : Window
{
    private readonly UiSettings _store = new();
    private UiPreferences _preferences;
    public SettingsWindow(Action<bool>? onModeChanged = null)
    {
        _preferences = _store.Load();
        UiText.Bind(this, TitleProperty, "设置");
        Width = 700; Height = 490; MinWidth = 620; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BackgroundBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var dock = new DockPanel { Margin = new Thickness(20) }; Content = dock;
        var close = new Button { HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 12, 0, 0) };
        UiText.Bind(close, ContentControl.ContentProperty, "关闭"); close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Bottom); dock.Children.Add(close);
        var tabs = new TabControl { TabStripPlacement = Dock.Left }; dock.Children.Add(tabs);
        var general = Page(tabs, "常规");
        Label(general, "界面语言");
        var languages = new ComboBox { MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
        var system = new ComboBoxItem { Tag = "system" }; UiText.Bind(system, ContentControl.ContentProperty, "跟随系统");
        languages.Items.Add(system); languages.Items.Add(new ComboBoxItem { Content = "简体中文", Tag = "zh-CN" }); languages.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        languages.SelectedIndex = _preferences.Language == "en" ? 2 : _preferences.Language == "zh-CN" ? 1 : 0;
        languages.SelectionChanged += (_, _) =>
        {
            _preferences = _preferences with { Language = (string)((ComboBoxItem)languages.SelectedItem).Tag };
            if (Save()) UiText.Current.SetLanguage(_preferences.Language);
        };
        general.Children.Add(languages);
        Label(general, "默认编辑模式");
        var mode = new ComboBox { MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var text in new[] { "普通模式", "专业模式" }) { var item = new ComboBoxItem(); UiText.Bind(item, ContentControl.ContentProperty, text); mode.Items.Add(item); }
        mode.SelectedIndex = _preferences.AdvancedMode ? 1 : 0;
        mode.SelectionChanged += (_, _) => { _preferences = _preferences with { AdvancedMode = mode.SelectedIndex == 1 }; if (Save()) onModeChanged?.Invoke(_preferences.AdvancedMode); }; general.Children.Add(mode);
        Label(general, "修改后立即生效并自动保存");
        var appearance = Page(tabs, "外观"); Label(appearance, "主题");
        var themes = new ComboBox { MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var theme in Enum.GetValues<AppTheme>()) { var item = new ComboBoxItem { Tag = theme }; UiText.Bind(item, ContentControl.ContentProperty, ThemeManager.Name(theme)); themes.Items.Add(item); if (theme == ThemeManager.CurrentTheme) themes.SelectedItem = item; }
        themes.SelectionChanged += (_, _) => ThemeManager.ApplyTheme((AppTheme)((ComboBoxItem)themes.SelectedItem).Tag); appearance.Children.Add(themes);
        Label(appearance, "自定义背景");
        var enabled = new CheckBox { IsChecked = _preferences.BackgroundEnabled }; UiText.Bind(enabled, ContentControl.ContentProperty, "启用背景图片"); appearance.Children.Add(enabled);
        enabled.Click += (_, _) => { _preferences = _preferences with { BackgroundEnabled = enabled.IsChecked == true }; if (Save()) BackgroundAppearance.Apply(); };
        var image = new System.Windows.Controls.Image { Height = 90, Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(0, 8, 0, 8) }; appearance.Children.Add(image);
        void Preview() { try { image.Source = File.Exists(_preferences.BackgroundImage) ? BackgroundAppearance.Load(_preferences.BackgroundImage!) : null; } catch { image.Source = null; } }
        Preview();
        var imageButtons = new StackPanel { Orientation = Orientation.Horizontal }; appearance.Children.Add(imageButtons);
        var choose = new Button { Padding = new Thickness(12,6,12,6) }; UiText.Bind(choose, ContentControl.ContentProperty, "选择 / 替换图片"); imageButtons.Children.Add(choose);
        choose.Click += (_, _) => {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            try {
                var bitmap = BackgroundAppearance.Load(dialog.FileName);
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WarnoLiteModdingTool"); Directory.CreateDirectory(dir);
                var destination = Path.Combine(dir, "background.png"); var temporary = destination + ".tmp";
                using (var stream = File.Create(temporary)) { var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); encoder.Save(stream); }
                File.Move(temporary, destination, true);
                _preferences = _preferences with { BackgroundImage = destination, BackgroundEnabled = true }; enabled.IsChecked = true;
                if (Save()) { Preview(); BackgroundAppearance.Apply(); }
            } catch (Exception ex) { MessageBox.Show(this, "无法读取该图片：" + ex.Message, "自定义背景"); }
        };
        var remove = new Button { Padding = new Thickness(12,6,12,6), Margin = new Thickness(8,0,0,0) }; UiText.Bind(remove, ContentControl.ContentProperty, "移除图片"); imageButtons.Children.Add(remove);
        remove.Click += (_, _) => { _preferences = _preferences with { BackgroundImage = null }; if (Save()) { Preview(); BackgroundAppearance.Apply(); } };
        Label(appearance, "图片不透明度（0–30%）");
        var opacity = new Slider { Minimum = 0, Maximum = 30, Value = Math.Clamp(_preferences.BackgroundOpacity * 100, 0, 30), TickFrequency = 1, IsSnapToTickEnabled = true, AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft };
        opacity.ValueChanged += (_, _) => { _preferences = _preferences with { BackgroundOpacity = opacity.Value / 100 }; if (Save()) BackgroundAppearance.Apply(); }; appearance.Children.Add(opacity);
        var layout = new ComboBox(); foreach (var (key, title) in new[] { ("fill", "铺满裁剪"), ("fit", "完整显示"), ("tile", "平铺") }) { var item = new ComboBoxItem { Tag = key }; UiText.Bind(item, ContentControl.ContentProperty, title); layout.Items.Add(item); if (key == _preferences.BackgroundLayout) layout.SelectedItem = item; }
        layout.SelectionChanged += (_, _) => { _preferences = _preferences with { BackgroundLayout = (string)((ComboBoxItem)layout.SelectedItem).Tag }; if (Save()) BackgroundAppearance.Apply(); }; appearance.Children.Add(layout);
        var about = Page(tabs, "关于"); Label(about, "WARNO Lite Modding Tool"); Label(about, "1.9.1 · Windows x64 · .NET 8");
        Link(about, "发布说明", Path.Combine(AppContext.BaseDirectory, "发布说明.md"));
        Link(about, "打开工具日志目录", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WarnoLiteModdingTool", "logs"));
    }
    private bool Save()
    {
        try { _store.Save(_preferences); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, ex.Message, UiText.T("设置"), MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }
    private static StackPanel Page(TabControl tabs, string title)
    {
        var panel = new StackPanel { Margin = new Thickness(22, 8, 12, 8) };
        var item = new TabItem { Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Padding = new Thickness(18, 12, 18, 12) };
        UiText.Bind(item, HeaderedContentControl.HeaderProperty, title); tabs.Items.Add(item); return panel;
    }
    private static void Label(Panel panel, string text)
    {
        var label = new TextBlock { Margin = new Thickness(0, 16, 0, 8), TextWrapping = TextWrapping.Wrap };
        UiText.Bind(label, TextBlock.TextProperty, text); panel.Children.Add(label);
    }
    private void Link(Panel panel, string text, string path)
    {
        var button = new Button { Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(12, 8, 12, 8) }; UiText.Bind(button, ContentControl.ContentProperty, text);
        button.Click += (_, _) =>
        {
            try { if (File.Exists(path) || Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { MessageBox.Show(this, ex.Message, UiText.T("关于")); }
        }; panel.Children.Add(button);
    }
}
