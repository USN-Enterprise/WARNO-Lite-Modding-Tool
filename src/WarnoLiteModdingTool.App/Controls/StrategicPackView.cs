using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Strategic;
namespace WarnoLiteModdingTool.App.Controls;
public sealed class StrategicPackView : UserControl
{
    private StrategicPackWorkspaceViewModel? _vm;
    private PackListItem? _item;
    private ListBox? _list;
    private ContentControl? _detail;
    private Grid? _body;
    private Button? _back, _open;
    private TextBlock? _count;
    private FacetFilter? _filter;
    private bool _busy, _selecting, _showDetail;
    public StrategicPackView()
    {
        DataContextChanged+=(_,_)=>Build();
        SizeChanged+=(_,_)=>Layout();
        System.ComponentModel.PropertyChangedEventManager.AddHandler(UiText.Current,(_,_)=>Build(),nameof(UiText.Version));
    }
    private static TextBlock Label(string text)=>new() {Text=UiText.T(text),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4)};
    private static Button Action(string text)=>new() {Content=UiText.T(text),Margin=new Thickness(0,0,8,0),Padding=new Thickness(12,6,12,6)};
    private void Build()
    {
        if(_vm is not null)System.Collections.Specialized.CollectionChangedEventManager.RemoveHandler(_vm.View,Changed);
        if(!ReferenceEquals(_vm,DataContext))_filter=null;
        _vm=DataContext as StrategicPackWorkspaceViewModel;
        if(_vm is not {} vm){Content=null;return;}
        var root=new DockPanel {Margin=new Thickness(16)};Content=root;
        var toolbar=new DockPanel {Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(toolbar,Dock.Top);root.Children.Add(toolbar);
        _count=new TextBlock {VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0)};DockPanel.SetDock(_count,Dock.Right);toolbar.Children.Add(_count);
        if(_filter is null){_filter=new FacetFilter();_filter.SetBinding(FacetFilter.RowsProperty,new Binding("FilterRows"));var created=_filter;created.FilterChanged+=(_,_)=>{vm.Filter=created.Matches;vm.View.Refresh();};}
        var filter=_filter;if(filter.Parent is Panel oldParent)oldParent.Children.Remove(filter);DockPanel.SetDock(filter,Dock.Right);toolbar.Children.Add(filter);
        var search=new TextBox {Margin=new Thickness(0,0,10,0),ToolTip=UiText.T("搜索名称、单位、运输")};search.SetBinding(TextBox.TextProperty,new Binding("Search"){UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});toolbar.Children.Add(search);
        _back=Action("返回列表");_back.HorizontalAlignment=HorizontalAlignment.Left;_back.Click+=(_,_)=>{_showDetail=false;Layout();};DockPanel.SetDock(_back,Dock.Top);root.Children.Add(_back);
        _open=Action("查看详情");_open.HorizontalAlignment=HorizontalAlignment.Left;_open.Click+=(_,_)=>{_showDetail=true;Layout();};DockPanel.SetDock(_open,Dock.Top);root.Children.Add(_open);
        _body=new Grid();_body.ColumnDefinitions.Add(new ColumnDefinition());_body.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(14)});_body.ColumnDefinitions.Add(new ColumnDefinition());root.Children.Add(_body);
        _list=new ListBox {HorizontalContentAlignment=HorizontalAlignment.Stretch};_list.SetBinding(ItemsControl.ItemsSourceProperty,new Binding("View"));
        var template=new DataTemplate();var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(MarginProperty,new Thickness(6));
        foreach(var (path,bold) in new[]{("UnitDisplay",true),("TransportDisplay",false),("Name",false)})
        {
            var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding(path));text.SetBinding(ToolTipProperty,new Binding(path));text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);if(bold)text.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);row.AppendChild(text);
        }
        var summary=new FrameworkElementFactory(typeof(TextBlock));var binding=new MultiBinding {StringFormat=UiText.T("老练度")+" {0}  ·  {1}"};binding.Bindings.Add(new Binding("Xp"));binding.Bindings.Add(new Binding("Status"));summary.SetBinding(TextBlock.TextProperty,binding);row.AppendChild(summary);template.VisualTree=row;_list.ItemTemplate=template;
        _list.MouseDoubleClick+=(_,_)=>{if(_item is not null){_showDetail=true;Layout();}};
        _list.SelectionChanged+=(_,_)=>{if(_selecting||_busy)return;var next=_list.SelectedItem as PackListItem;Dispatcher.BeginInvoke(new Action(async()=>await SelectAsync(next)));};_body.Children.Add(_list);
        _detail=new ContentControl {HorizontalContentAlignment=HorizontalAlignment.Stretch};Grid.SetColumn(_detail,2);_body.Children.Add(_detail);
        _item=vm.Selected;_selecting=true;_list.SelectedItem=_item;_selecting=false;Details();Layout();Count();
        System.Collections.Specialized.CollectionChangedEventManager.AddHandler(vm.View,Changed);
    }
    private void Changed(object? sender,System.Collections.Specialized.NotifyCollectionChangedEventArgs e)=>Count();
    private void Count(){if(_count is not null&&_vm is {} vm)_count.Text=UiText.T("显示")+" "+vm.View.Cast<object>().Count()+" / "+vm.Items.Count+" · "+UiText.T("草稿")+" "+vm.Items.Count(p=>p.Status.Length>0);}
    private void Layout()
    {
        if(_body is null||_list is null||_detail is null||_back is null)return;
        var narrow=ActualWidth<1000;
        _list.Visibility=narrow&&_showDetail?Visibility.Collapsed:Visibility.Visible;
        _detail.Visibility=narrow&&!_showDetail?Visibility.Collapsed:Visibility.Visible;
        _back.Visibility=narrow&&_showDetail?Visibility.Visible:Visibility.Collapsed;
        if(_open is not null){_open.Visibility=narrow&&!_showDetail?Visibility.Visible:Visibility.Collapsed;_open.IsEnabled=_item is not null;}
        _body.ColumnDefinitions[0].Width=narrow?new GridLength(_showDetail?0:1,GridUnitType.Star):new GridLength(0.38,GridUnitType.Star);
        _body.ColumnDefinitions[1].Width=new GridLength(narrow?0:14);
        _body.ColumnDefinitions[2].Width=narrow?new GridLength(_showDetail?1:0,GridUnitType.Star):new GridLength(0.62,GridUnitType.Star);
    }
    private async Task SelectAsync(PackListItem? next)
    {
        if(_selecting||_busy||next is null||_vm is not {} vm)return;
        var previous=_item;
        if(previous is not null && previous.Id!=next.Id && vm.PendingEdits.TryGetValue(previous.Id,out var pending) && pending!=previous.State)
        {
            _selecting=true;_list!.SelectedItem=previous;_selecting=false;
            var choice=ConfirmSwitch();
            if(choice==MessageBoxResult.Cancel)return;
            if(choice==MessageBoxResult.Yes && !await SaveAsync())return;
            vm.PendingEdits.Remove(previous.Id);
        }
        _item=vm.Items.FirstOrDefault(p=>p.Id==next.Id)??next;vm.Selected=_item;
        _selecting=true;_list!.SelectedItem=_item;_selecting=false;_showDetail=true;Details();Layout();
    }
    private MessageBoxResult ConfirmSwitch()
    {
        var result=MessageBoxResult.Cancel;
        var window=new Window {Owner=Window.GetWindow(this),Title=UiText.T("未保存编辑"),Width=510,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        window.SetResourceReference(BackgroundProperty,"SurfaceBrush");window.SetResourceReference(ForegroundProperty,"TextBrush");
        var panel=new StackPanel {Margin=new Thickness(20)};window.Content=panel;panel.Children.Add(Label("当前 Pack 有未加入草稿的修改。"));
        var actions=new WrapPanel {Margin=new Thickness(0,12,0,0)};panel.Children.Add(actions);
        foreach(var (label,value) in new[]{("加入草稿并切换",MessageBoxResult.Yes),("放弃并切换",MessageBoxResult.No),("取消",MessageBoxResult.Cancel)})
        {var button=Action(label);button.Click+=(_,_)=>{result=value;window.Close();};actions.Children.Add(button);}
        window.ShowDialog();return result;
    }
    private async Task<bool> SaveAsync()
    {
        if(_busy||_vm is not {} vm||_item is not {} item)return false;
        _busy=true;IsEnabled=false;
        try
        {
            await vm.SaveAsync(item,vm.PendingEdits.GetValueOrDefault(item.Id,item.State));vm.PendingEdits.Remove(item.Id);
            _item=vm.Items.FirstOrDefault(p=>p.Id==item.Id);vm.Selected=_item;
            _selecting=true;_list!.SelectedItem=_item;_selecting=false;Details();return true;
        }
        catch(Exception ex){MessageBox.Show(Window.GetWindow(this),ex.Message,UiText.T("未保存编辑"),MessageBoxButton.OK,MessageBoxImage.Warning);return false;}
        finally{_busy=false;IsEnabled=true;}
    }
    private void Details()
    {
        if(_detail is null||_vm is not {} vm)return;
        if(_item is not {} item){_detail.Content=Label("请选择 Pack");return;}
        var state=vm.PendingEdits.GetValueOrDefault(item.Id,item.State);
        var panel=new StackPanel {Margin=new Thickness(4,0,4,10)};_detail.Content=new ScrollViewer {Content=panel,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock {Text=item.UnitDisplay,FontSize=19,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(Label("Pack 名称"));
        var name=new TextBox {Text=state.Name,ToolTip=state.Name};panel.Children.Add(name);
        var nameActions=new WrapPanel {Margin=new Thickness(0,6,0,4)};panel.Children.Add(nameActions);
        var readable=Action("生成可读名称");nameActions.Children.Add(readable);var copy=Action("复制名称");copy.Click+=(_,_)=>{try{Clipboard.SetText(name.Text);}catch(Exception ex){MessageBox.Show(ex.Message);}};nameActions.Children.Add(copy);
        panel.Children.Add(Label("单位"));var units=new SearchPicker {EnableUnitFilters=true,ItemsSource=vm.Data.Units.Units,DisplayMemberPath="DisplayName",SecondaryMemberPath="Name",SelectedItem=vm.Data.Units.Units.FirstOrDefault(u=>u.Name==state.Unit)};panel.Children.Add(units);
        panel.Children.Add(Label("运输"));var transports=new SearchPicker {EnableUnitFilters=true,ItemsSource=vm.Data.Units.Units.Where(u=>u.HasUniqueTransporterModule).ToArray(),DisplayMemberPath="DisplayName",SecondaryMemberPath="Name",SelectedItem=vm.Data.Units.Units.FirstOrDefault(u=>u.Name==state.Transport)};panel.Children.Add(transports);
        var noTransport=new CheckBox {Content=UiText.T("无运输"),IsChecked=state.Transport.Length==0,Margin=new Thickness(0,6,0,0)};panel.Children.Add(noTransport);transports.IsEnabled=noTransport.IsChecked!=true;
        panel.Children.Add(Label("老练度"));var xp=new ComboBox {ItemsSource=new[]{0,1,2,3}.Append(state.Xp).Distinct().ToArray(),SelectedItem=state.Xp};panel.Children.Add(xp);
        var status=Label("");panel.Children.Add(status);
        StrategicPackEdit Current()=>new(name.Text.Trim(),(units.SelectedItem as Core.Units.UnitRecord)?.Name??state.Unit,noTransport.IsChecked==true?"":(transports.SelectedItem as Core.Units.UnitRecord)?.Name??state.Transport,(int)(xp.SelectedItem??state.Xp));
        void Track(){var current=Current();if(current==item.State)vm.PendingEdits.Remove(item.Id);else vm.PendingEdits[item.Id]=current;status.Text=current==item.State?UiText.T(item.Status):UiText.T("未加入草稿");status.Visibility=status.Text.Length==0?Visibility.Collapsed:Visibility.Visible;}
        name.TextChanged+=(_,_)=>Track();units.SelectedItemChanged+=(_,_)=>Track();transports.SelectedItemChanged+=(_,_)=>Track();xp.SelectionChanged+=(_,_)=>Track();
        noTransport.Checked+=(_,_)=>{transports.IsEnabled=false;Track();};noTransport.Unchecked+=(_,_)=>{transports.IsEnabled=true;Track();};Track();
        readable.Click+=(_,_)=>{try{name.Text=vm.Suggest(Current());}catch(Exception ex){status.Text=ex.Message;}};
        var impact=Label(vm.Impact);panel.Children.Add(impact);
        var actions=new WrapPanel {Margin=new Thickness(0,6,0,14)};panel.Children.Add(actions);
        var undo=Action("撤销编辑");undo.Click+=(_,_)=>{vm.PendingEdits.Remove(item.Id);Details();};actions.Children.Add(undo);
        var save=Action("加入草稿");save.IsEnabled=vm.CanEdit;save.Click+=async(_,_)=>await SaveAsync();actions.Children.Add(save);
        var title=Label("使用位置");title.FontWeight=FontWeights.SemiBold;panel.Children.Add(title);
        var uses=vm.Uses;
        if(uses.Count==0)panel.Children.Add(Label(vm.Data.HasRoster?"尚未被引用":"编制资料不完整，统计仅限已识别文件"));
        else
        {
            var grid=new DataGrid {ItemsSource=uses,Height=190,AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false};
            foreach(var (label,path) in new[]{("营团","Battalion"),("所属连 / 排","Group"),("槽位","Slot")})grid.Columns.Add(new DataGridTextColumn {Header=UiText.T(label),Binding=new Binding(path),Width=new DataGridLength(path=="Slot"?0.5:1,DataGridLengthUnitType.Star)});
            ResponsiveColumns.SetEnabled(grid,true);panel.Children.Add(grid);
            var jump=Action("跳转到将军模式");jump.HorizontalAlignment=HorizontalAlignment.Left;jump.Click+=(_,_)=>{if(grid.SelectedItem is StrategicPackUse u)Jump(u);};panel.Children.Add(jump);
            grid.SelectionChanged+=(_,_)=>jump.IsEnabled=grid.SelectedItem is StrategicPackUse u && u.PawnId.Length>0;grid.SelectedIndex=0;
            var technical=new Expander {Header="Deck / Pawn",Margin=new Thickness(0,8,0,0)};var id=new TextBlock {TextWrapping=TextWrapping.Wrap};grid.SelectionChanged+=(_,_)=>{if(grid.SelectedItem is StrategicPackUse u)id.Text=u.Deck+" / "+u.PawnId;};if(grid.SelectedItem is StrategicPackUse first)id.Text=first.Deck+" / "+first.PawnId;technical.Content=id;panel.Children.Add(technical);
        }
    }
    private void Jump(StrategicPackUse u)
    {
        if(Window.GetWindow(this)?.DataContext is MainViewModel main&&main.StrategicWorkspace is {} strategic)
        {
            var target=strategic.Data.Records.FirstOrDefault(r=>r.Deck.Info.Name==u.Deck&&r.Pawn?.Info.Name==u.PawnId);
            if(target is not null){strategic.Selected=target;main.SelectedModule=main.Modules.FirstOrDefault(m=>m.Key=="strategic");}
        }
    }
}
