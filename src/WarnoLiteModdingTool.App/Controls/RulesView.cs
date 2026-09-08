using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.App.Localisation;
namespace WarnoLiteModdingTool.App.Controls;
public sealed class RulesView : UserControl
{
    public static readonly DependencyProperty WorkspaceProperty=DependencyProperty.Register(nameof(Workspace),typeof(RulesWorkspaceViewModel),typeof(RulesView),new PropertyMetadata(null,(o,e)=>((RulesView)o).Build()));
    public RulesWorkspaceViewModel? Workspace {get=>(RulesWorkspaceViewModel?)GetValue(WorkspaceProperty);set=>SetValue(WorkspaceProperty,value);}
    public RulesView()
    {
        System.ComponentModel.PropertyChangedEventManager.AddHandler(UiText.Current, (_,e)=> { if(e.PropertyName==nameof(UiText.Version))Workspace?.Refresh(); }, nameof(UiText.Version));
    }
    private void Build()
    {
        if(Workspace is not {} vm){Content=null;return;}
        var panel=new DockPanel{Margin=new Thickness(15)};
        var search=new TextBox{Margin=new Thickness(0,0,0,10),ToolTip=UiText.T("搜索游戏规则")};search.TextChanged+=(_,_)=>{vm.Search=search.Text;vm.Refresh();};DockPanel.SetDock(search,Dock.Top);panel.Children.Add(search);
        var note=new TextBlock{Text=UiText.T("修改保存为草稿；在草稿中心应用。参战名额变化会联动表长，新增列沿用末列值。"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(note,Dock.Top);UiText.Bind(note,TextBlock.TextProperty,"修改保存为草稿；在草稿中心应用。参战名额变化会联动表长，新增列沿用末列值。");panel.Children.Add(note);
        var list=new StackPanel();
        void Render()
        {
            list.Children.Clear();
            foreach(var group in vm.View.Cast<RuleGroupViewModel>())
            {
                var expander=new Expander{Header=group.Title,Margin=new Thickness(0,0,0,8),Padding=new Thickness(7)};
                expander.SetResourceReference(BackgroundProperty,"SurfaceBrush");
                expander.Expanded+=(_,_)=>{if(expander.Content is null)expander.Content=Editor(group);};
                list.Children.Add(expander);
            }
        }
        vm.View.CollectionChanged+=(_,_)=>Render();Render();
        panel.Children.Add(new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=panel;
    }
    private static UIElement Editor(RuleGroupViewModel group)
    {
        var panel=new StackPanel();var status=new TextBlock{TextWrapping=TextWrapping.Wrap};var statusBinding=new MultiBinding{Converter=new LocalizedValueConverter()};statusBinding.Bindings.Add(new Binding(nameof(group.Status)){Source=group});statusBinding.Bindings.Add(new Binding(nameof(UiText.Version)){Source=UiText.Current});status.SetBinding(TextBlock.TextProperty,statusBinding);panel.Children.Add(status);
        var cells=new WrapPanel{IsEnabled=group.CanEdit};panel.Children.Add(cells);
        foreach(var cell in group.Cells)
        {
            var item=new StackPanel{Width=210,Margin=new Thickness(0,5,10,8)};cells.Children.Add(item);
            item.Children.Add(new TextBlock{Text=UiText.T(cell.Cell.Label),TextWrapping=TextWrapping.Wrap,ToolTip=cell.Cell.Key});
            item.Children.Add(new ParameterNote { Text = cell.Cell.Key + (group.Group.Definition.MapKey is {} key ? "[" + key + "]" : "") });
            if(cell.Cell.Boolean)
            {
                var choice=new ComboBox{ItemsSource=new[]{new{Label=UiText.T("是"),Value="true"},new{Label=UiText.T("否"),Value="false"}},DisplayMemberPath="Label",SelectedValuePath="Value"};
                choice.SetBinding(ComboBox.SelectedValueProperty,new Binding(nameof(cell.Value)){Source=cell,Mode=BindingMode.TwoWay});item.Children.Add(choice);
            }
            else
            {
                var input=new TextBox();input.SetBinding(TextBox.TextProperty,new Binding(nameof(cell.Value)){Source=cell,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});item.Children.Add(input);
            }
        }
        var undo=new Button{Content=UiText.T("撤销此项"),HorizontalAlignment=HorizontalAlignment.Left};undo.Click+=async(_,_)=>await group.UndoAsync();panel.Children.Add(undo);return panel;
    }
}
