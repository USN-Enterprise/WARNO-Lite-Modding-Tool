using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Settings;
namespace WarnoLiteModdingTool.App.Controls;
public static class FloatingPanels
{
    private static readonly List<Window> Windows=[];
    private static readonly HashSet<MainWindow> Installed=[];
    private static readonly HashSet<Expander> DetachedGroups=[];
    private static readonly Dictionary<MainWindow,Func<UiPreferences>> Readers=[];
    public static MainWindow Host(DependencyObject item){var w=Window.GetWindow(item);while(w is not null){if(w is MainWindow m)return m;w=w.Owner;}throw new InvalidOperationException("主窗口已关闭");}
    public static void CloseAll(){foreach(var w in Windows.ToArray())w.Close();}
    public static void Install(MainWindow host, Func<UiPreferences>? readSettings = null)
    {
        if(readSettings is not null)Readers[host]=readSettings;
        else Readers.TryAdd(host,()=>new UiSettings().Load());
        if(!Installed.Add(host))return;
        readSettings = () => Readers[host]();
        var views=Descendants(host).OfType<UserControl>().Where(v=>v.GetType().Namespace=="WarnoLiteModdingTool.App.Workspaces"||v is RulesView or StrategicWorkspaceView or StrategicPackView).ToArray();
        foreach(var view in views)
        {
            if(view.Parent is not Grid parent)continue;var index=parent.Children.IndexOf(view);var row=Grid.GetRow(view);var column=Grid.GetColumn(view);var visibility=BindingOperations.GetBindingBase(view,UIElement.VisibilityProperty);var wrapper=new DockPanel();Grid.SetRow(wrapper,row);Grid.SetColumn(wrapper,column);Grid.SetRowSpan(wrapper,Grid.GetRowSpan(view));Grid.SetColumnSpan(wrapper,Grid.GetColumnSpan(view));
            parent.Children.RemoveAt(index);parent.Children.Insert(index,wrapper);var title=new TextBlock{Text=UiText.T(Name(view)),Margin=new Thickness(8,3,8,3),FontWeight=FontWeights.SemiBold,Cursor=Cursors.Hand,ToolTip=UiText.T("双击标题独立弹出")};DockPanel.SetDock(title,Dock.Top);wrapper.Children.Add(title);wrapper.Children.Add(view);
            // Keep the visibility rule on the slot. The detached page stays usable after module navigation.
            if(visibility is not null){BindingOperations.ClearBinding(view,UIElement.VisibilityProperty);wrapper.SetBinding(UIElement.VisibilityProperty,visibility);view.Visibility=Visibility.Visible;}
            title.MouseLeftButtonDown+=(_,e)=>{var prefs=readSettings();if(e.ClickCount!=2||!prefs.FloatingPanels||prefs.FloatSmallPanels)return;e.Handled=true;if(view.Parent!=wrapper)return;var context=view.DataContext;wrapper.Children.Remove(view);var placeholder=new TextBlock{Text=UiText.T("面板已独立打开，关闭窗口后归位"),Margin=new Thickness(20)};wrapper.Children.Add(placeholder);var ownData=view.ReadLocalValue(FrameworkElement.DataContextProperty);var binding=BindingOperations.GetBindingBase(view,FrameworkElement.DataContextProperty);view.DataContext=context;Open(host,view,Name(view),()=>{wrapper.Children.Remove(placeholder);RestoreContext(view,ownData,binding);wrapper.Children.Add(view);},readSettings);};
        }
        if(host.DataContext is System.ComponentModel.INotifyPropertyChanged notify) notify.PropertyChanged+=(_,e)=>{if(e.PropertyName is "IsScanning" or "AdvancedMode" or "UnitWorkspace")CloseAll();};
        host.AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler((_,e)=>Small(host,e,readSettings)),true);host.Closed+=(_,_)=>{CloseAll();Installed.Remove(host);Readers.Remove(host);};
    }
    private static string Name(UserControl v)=>v.GetType().Name switch {"UnitWorkspaceView"=>"单位","WeaponWorkspaceView"=>"武器","AmmoWorkspaceView"=>"弹药","DivisionWorkspaceView"=>"战术师","RulesView"=>"游戏规则","StrategicWorkspaceView"=>"将军模式","StrategicPackView"=>"战略 Pack","DraftWorkspaceView"=>"草稿","ProblemWorkspaceView"=>"问题",_=>"对象"};
    private static void Small(MainWindow host,MouseButtonEventArgs e,Func<UiPreferences> readSettings)
    {
        var prefs=readSettings();if(e.ChangedButton!=MouseButton.Left||e.ClickCount!=2||!prefs.FloatingPanels||!prefs.FloatSmallPanels )return;
        var source=e.OriginalSource as DependencyObject;var exp=Ancestor<Expander>(source);if(exp?.Content is not FrameworkElement content||!exp.IsEnabled||DetachedGroups.Contains(exp))return;
        if(source is not null&&(source==content||IsWithin(source,content)))return;
        var local=content.ReadLocalValue(FrameworkElement.DataContextProperty);var binding=BindingOperations.GetBindingBase(content,FrameworkElement.DataContextProperty);var context=content.DataContext;
        exp.Content=null;var placeholder=new TextBlock{Text=UiText.T("面板已独立打开，关闭窗口后归位"),Margin=new Thickness(10)};exp.Content=placeholder;content.DataContext=context;e.Handled=true;
        DetachedGroups.Add(exp);
        Window? floating=null;
        DependencyPropertyChangedEventHandler changed=(_,_)=>floating?.Close();
        RoutedEventHandler unloaded=(_,_)=>floating?.Close();
        floating=Open(host,content,exp.Header is string text?text:"面板",()=>{exp.DataContextChanged-=changed;exp.Unloaded-=unloaded;exp.Content=null;RestoreContext(content,local,binding);exp.Content=content;DetachedGroups.Remove(exp);},readSettings);
        exp.DataContextChanged+=changed;exp.Unloaded+=unloaded;
    }
    private static void RestoreContext(FrameworkElement element,object local,BindingBase? binding){if(binding is not null)element.SetBinding(FrameworkElement.DataContextProperty,binding);else if(local==DependencyProperty.UnsetValue)element.ClearValue(FrameworkElement.DataContextProperty);else element.DataContext=local;}
    private static Window Open(MainWindow host,FrameworkElement content,string title,Action restore,Func<UiPreferences> readSettings)
    {
        var w=new Window{Owner=host,Title=UiText.T(title),Width=1100,Height=760,MinWidth=550,MinHeight=400,DataContext=host.DataContext,WindowStartupLocation=WindowStartupLocation.CenterOwner};w.SetResourceReference(Window.BackgroundProperty,"SurfaceBrush");w.SetResourceReference(Window.ForegroundProperty,"TextBrush");w.SetBinding(UIElement.IsEnabledProperty,new Binding("CanInteract"));w.Content=content;Windows.Add(w);w.Closed+=(_,_)=>{w.Content=null;restore();Windows.Remove(w);};w.AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler((_,e)=>Small(host,e,readSettings)),true);w.Show();return w;
    }
    private static T? Ancestor<T>(DependencyObject? value) where T:DependencyObject{while(value is not null){if(value is T t)return t;value=value is Visual?VisualTreeHelper.GetParent(value):LogicalTreeHelper.GetParent(value);}return null;}
    private static bool IsWithin(DependencyObject value,DependencyObject parent){while(value is not null){if(value==parent)return true;value=value is Visual?VisualTreeHelper.GetParent(value):LogicalTreeHelper.GetParent(value);}return false;}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject o){for(var i=0;i<VisualTreeHelper.GetChildrenCount(o);i++){var c=VisualTreeHelper.GetChild(o,i);yield return c;foreach(var d in Descendants(c))yield return d;}}
}
