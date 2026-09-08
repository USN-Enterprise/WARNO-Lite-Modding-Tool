using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels;
namespace WarnoLiteModdingTool.App.Controls;
public sealed class UnitSelectionWindow:Window
{
    public sealed class Option(string id,string label,bool selected):ObservableObject {private bool _selected=selected;public string Id=>id;public string Label=>label;public bool Selected{get=>_selected;set=>SetProperty(ref _selected,value);}}
    public IReadOnlyList<string> SelectedIds=>_options.Where(o=>o.Selected).Select(o=>o.Id).ToArray();
    private readonly Option[] _options;
    public UnitSelectionWindow(IEnumerable<(string Id,string Label)> candidates,IEnumerable<string> selected)
    {
        var chosen=selected.ToHashSet();_options=candidates.Concat(chosen.Select(id=>(id,id))).DistinctBy(c=>c.Item1).Select(c=>new Option(c.Item1,c.Item2,chosen.Contains(c.Item1))).ToArray();
        Title=UiText.T("选择单位");Width=640;Height=550;WindowStartupLocation=WindowStartupLocation.CenterOwner;SetResourceReference(BackgroundProperty,"SurfaceBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var root=new DockPanel {Margin=new Thickness(16)};Content=root;var search=new TextBox {Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(search,Dock.Top);root.Children.Add(search);
        var actions=new StackPanel {Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var view=new ListCollectionView(_options);view.Filter=o=>o is Option option&&(option.Label.Contains(search.Text,StringComparison.OrdinalIgnoreCase)||option.Id.Contains(search.Text,StringComparison.OrdinalIgnoreCase));search.TextChanged+=(_,_)=>view.Refresh();
        var list=new ListBox {ItemsSource=view};VirtualizingPanel.SetIsVirtualizing(list,true);VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);root.Children.Add(list);
        var t=new DataTemplate();var c=new FrameworkElementFactory(typeof(CheckBox));c.SetBinding(ContentControl.ContentProperty,new Binding("Label"));c.SetBinding(ToolTipProperty,new Binding("Id"));c.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding("Selected"){Mode=BindingMode.TwoWay});t.VisualTree=c;list.ItemTemplate=t;
        void Button(string label,Action action){var b=new Button {Content=UiText.T(label),Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,10,8,0)};b.Click+=(_,_)=>action();actions.Children.Add(b);}
        Button("全选当前筛选",()=>{foreach(Option o in view)o.Selected=true;});Button("取消全选",()=>{foreach(Option o in view)o.Selected=false;});Button("应用选择",()=>DialogResult=true);Button("取消",Close);
    }
}
