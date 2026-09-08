using System.Windows;
using System.Windows.Controls;
namespace WarnoLiteModdingTool.App.Localisation;
public static class LocalizedMessageBox
{
    public static MessageBoxResult Show(Window owner,string text,string caption,MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage image=MessageBoxImage.None)=>Display(owner,text,caption,buttons);
    public static MessageBoxResult Show(string text,string caption="",MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage image=MessageBoxImage.None)=>Display(Application.Current?.MainWindow,text,caption,buttons);
    private static MessageBoxResult Display(Window? owner,string text,string caption,MessageBoxButton buttons)
    {
        var result=buttons is MessageBoxButton.YesNo?MessageBoxResult.No:MessageBoxResult.Cancel;
        var window=new Window {Title=UiText.T(caption),Width=680,SizeToContent=SizeToContent.Height,MaxHeight=720,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,WindowStartupLocation=owner?.IsVisible==true?WindowStartupLocation.CenterOwner:WindowStartupLocation.CenterScreen};
        if(owner?.IsVisible==true)window.Owner=owner;
        window.SetResourceReference(Window.BackgroundProperty,"SurfaceBrush");window.SetResourceReference(Window.ForegroundProperty,"TextBrush");
        var panel=new StackPanel {Margin=new Thickness(24)};window.Content=panel;
        panel.Children.Add(new TextBlock {Text=UiText.T(caption),FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,16)});
        panel.Children.Add(new ScrollViewer {MaxHeight=480,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new TextBlock {Text=UiText.T(text),TextWrapping=TextWrapping.Wrap,LineHeight=23}});
        var actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};panel.Children.Add(actions);
        void Add(string label,MessageBoxResult value,bool primary=false){var button=new Button {Content=UiText.T(label),Padding=new Thickness(18,9,18,9),Margin=new Thickness(8,0,0,0),IsDefault=primary};if(primary)button.SetResourceReference(Control.BackgroundProperty,"PrimaryBrush");button.Click+=(_,_)=>{result=value;window.Close();};actions.Children.Add(button);}
        if(buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel){if(buttons==MessageBoxButton.YesNoCancel){Add("取消",MessageBoxResult.Cancel);Add("否",MessageBoxResult.No);}else Add("取消",MessageBoxResult.No);Add(caption.Contains("应用")?"应用草稿":"确认",MessageBoxResult.Yes,true);}
        else {if(buttons==MessageBoxButton.OKCancel)Add("取消",MessageBoxResult.Cancel);Add("确定",MessageBoxResult.OK,true);}
        window.ShowDialog();return result;
    }
}
