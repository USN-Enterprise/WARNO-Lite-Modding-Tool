using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace WarnoLiteModdingTool.App.Controls;

public sealed class WindowTitleBar : Border
{
    public WindowTitleBar()
    {
        SetResourceReference(BackgroundProperty, "SurfaceAltBrush");
        var panel = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        panel.Children.Add(buttons);
        AddButton(buttons, "─", "最小化", SystemCommands.MinimizeWindow);
        var maximize = AddButton(buttons, "□", "最大化 / 还原", window =>
        {
            if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
            else SystemCommands.MaximizeWindow(window);
        });
        AddButton(buttons, "×", "关闭", SystemCommands.CloseWindow);
        var icon = new Image { Width = 18, Height = 18, Margin = new Thickness(12, 0, 9, 0) };
        WindowChrome.SetIsHitTestVisibleInChrome(icon, true);
        icon.MouseLeftButtonDown += (_, e) =>
        {
            var window = Window.GetWindow(this);
            if (window is null) return;
            if (e.ClickCount == 2) SystemCommands.CloseWindow(window);
            else SystemCommands.ShowSystemMenu(window, icon.PointToScreen(new Point(0, icon.ActualHeight)));
        };
        panel.Children.Add(icon);
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };
        panel.Children.Add(title);
        Child = panel;
        Loaded += (_, _) =>
        {
            var window = Window.GetWindow(this);
            if (window is null) return;
            icon.Source = window.Icon;
            title.Text = window.Title;
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = 40,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
            WindowCorners.Attach(window);
            window.StateChanged += (_, _) => maximize.Content = window.WindowState == WindowState.Maximized ? "❐" : "□";
        };
    }

    private Button AddButton(Panel panel, string text, string label, Action<Window> action)
    {
        var button = new Button
        {
            Content = text, Width = 46, Height = 40, Margin = new Thickness(0),
            Padding = new Thickness(0), BorderThickness = new Thickness(0), FontSize = 17,
            Background = Brushes.Transparent, Focusable = false
        };
        Localisation.UiText.Bind(button, ToolTipProperty, label);
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        button.Click += (_, _) => { if (Window.GetWindow(this) is { } window) action(window); };
        panel.Children.Add(button);
        return button;
    }
}
