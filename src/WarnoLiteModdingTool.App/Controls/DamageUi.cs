using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Localisation;

namespace WarnoLiteModdingTool.App.Controls;

internal static class DamageUi
{
    public static TextBlock Text(string value, bool note = false)
    {
        var text = new TextBlock { Text=UiText.T(value), TextWrapping=TextWrapping.Wrap, Margin=new(0,4,0,8) };
        if(note) text.SetResourceReference(TextBlock.ForegroundProperty,"MutedTextBrush");
        return text;
    }
    public static TextBlock Heading(string value) => RulePresentation.Heading(UiText.T(value),"RuleObjectHeading");
    public static StackPanel Field(string label, FrameworkElement control, double width=230)
    {
        var panel=new StackPanel{Width=width,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,6,12,6)};
        panel.Children.Add(Text(label)); control.HorizontalAlignment=HorizontalAlignment.Stretch; panel.Children.Add(control); return panel;
    }
    public static Button Button(string label, Action action, string? tag=null)
    {
        var button=new Button{Content=UiText.T(label),Margin=new(0,5,8,5),Tag=tag};
        button.SetResourceReference(FrameworkElement.StyleProperty,"SecondaryButton"); button.Click+=(_,_)=>action(); return button;
    }
    public static Button AsyncButton(string label, Func<Task> action, Action<Exception> error, string? tag=null)
    {
        var button=Button(label,()=>{},tag);
        button.Click+=async(_,_)=>{button.IsEnabled=false;try{await action();}catch(Exception e){error(e);}finally{button.IsEnabled=true;}};
        return button;
    }
    public static DataGrid Table(params (string Title,string Property)[] columns)
    {
        var grid=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,
            HeadersVisibility=DataGridHeadersVisibility.Column,RowHeaderWidth=0,MinHeight=120,SelectionMode=DataGridSelectionMode.Extended};
        foreach(var c in columns) grid.Columns.Add(new DataGridTextColumn{Header=UiText.T(c.Title),Binding=new Binding(c.Property),Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=90,
            ElementStyle=(Style?)Application.Current.TryFindResource("EditorGridValueText")});
        return grid;
    }
    public static ScrollViewer Scroll(UIElement content) => new(){Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    public static void Window(Window window,string title)
    {
        window.Title=UiText.T(title);window.Width=1100;window.Height=820;window.MinWidth=760;window.MinHeight=550;
        window.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        window.SetResourceReference(Control.BackgroundProperty,"SurfaceBrush");window.SetResourceReference(Control.ForegroundProperty,"TextBrush");
    }
}
