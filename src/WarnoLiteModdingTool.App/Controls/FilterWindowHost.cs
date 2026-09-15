using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Localisation;
namespace WarnoLiteModdingTool.App.Controls;

// Shared launcher for secondary workspace and picker filters. Unit workspace keeps its own UI.
public class FilterWindowHost : ContentControl
{
    private readonly Button _button = new() { HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 6, 12, 6) };
    private object? _body;
    private Window? _window;
    private ContentControl? _holder;
    public Action? ClearFilters {get;set;}
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(nameof(Header), typeof(object), typeof(FilterWindowHost),
        new PropertyMetadata(null, (o, e) => ((FilterWindowHost)o)._button.Content = e.NewValue));
    public object Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public new object? Content
    {
        get => _body;
        set { _body = value; if (_holder is not null) _holder.Content = value; }
    }
    public FilterWindowHost()
    {
        base.Content = _button;
        _button.Click += (_, _) => Open();

    }
    public void Open()
    {
        if (_window is not null) { _window.Activate(); return; }
        var owner = Window.GetWindow(this) ?? Application.Current?.MainWindow;
        _window = new Window { Owner = owner, Title = UiText.T("筛选"), Width = 620, Height = 520,
            MinWidth = 400, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner, Padding = new Thickness(18) };
        _window.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        _window.SetResourceReference(ForegroundProperty, "TextBrush");
        var panel=new DockPanel();panel.SetResourceReference(Panel.BackgroundProperty,"SurfaceBrush");var heading=new TextBlock {Text=UiText.T("筛选"),FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(18,18,18,12)};DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);var actions=new StackPanel {Margin=new Thickness(18),Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Bottom);panel.Children.Add(actions);
        var clear=new Button {Content=UiText.T("清除筛选"),Margin=new Thickness(0,12,8,0)};clear.Click+=(_,_)=>ClearFilters?.Invoke();actions.Children.Add(clear);
        var close=new Button {Content=UiText.T("关闭"),Margin=new Thickness(0,12,0,0)};close.Click+=(_,_)=>_window?.Close();actions.Children.Add(close);
        _holder=new ContentControl {Content=_body,Margin=new Thickness(18,0,18,0)};panel.Children.Add(_holder);_window.Content=panel;
        try { _window.ShowDialog(); }
        finally { _holder.Content=null;_holder=null;_window.Content = null; _window = null; }
    }
}
