using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels;
using MessageBox = WarnoLiteModdingTool.App.Localisation.LocalizedMessageBox;
namespace WarnoLiteModdingTool.App.Controls;

public sealed class StrategicMappingWindow : Window
{
    public StrategicMappingWindow(StrategicWorkspaceViewModel vm)
    {
        UiText.Bind(this,TitleProperty,"Pack 与索引");Width=1100;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"BackgroundBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var root=new DockPanel { Margin=new Thickness(14) };Content=root;
        var actions=new StackPanel { Orientation=Orientation.Horizontal };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var status=new TextBlock { Text=UiText.T("修改当前编制；每行表示一个连续槽位区间。"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) };DockPanel.SetDock(status,Dock.Top);root.Children.Add(status);
        var editor=new StackPanel { Margin=new Thickness(0,8,0,8) };DockPanel.SetDock(editor,Dock.Bottom);root.Children.Add(editor);
        var rows=vm.MappingRows();var grid=new DataGrid { ItemsSource=rows,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,SelectionMode=DataGridSelectionMode.Single };
        void Column(string title,string path,bool readOnly=false,double width=100) => grid.Columns.Add(new DataGridTextColumn { Header=path is "Start" or "Count" or "Xp" ? ParameterNote.Header(title, path switch { "Start" => "PackIndexUnitNumberList · " + UiText.T("元组索引"), "Count" => "PackIndexUnitNumberList · " + UiText.T("元组数量"), "Xp" => "DeckPackDescriptor.Xp", _ => path }) : UiText.T(title),Binding=new Binding(path) { UpdateSourceTrigger=UpdateSourceTrigger.LostFocus,ValidatesOnExceptions=true },IsReadOnly=readOnly,Width=new DataGridLength(width) });
        Column("起始索引","Start",!Advanced.EditorMode.IsAdvanced,145);Column("数量","Count",false,145);Column("Pack","Pack",true,230);Column("单位","Unit",true,230);Column("运输","Transport",true,230);Column("老练度","Xp",false,135);Column("所属连 / 排","Group",true,260);root.Children.Add(grid);
        var original=rows.Select(r=>(r.Start,r.Count,r.Pack,r.Unit,r.Transport,r.Xp)).ToArray();
        grid.SelectionChanged+=(_,_)=> {
            editor.Children.Clear();if(grid.SelectedItem is not StrategicMappingRow row)return;
            editor.Children.Add(new ParameterNote { Text="DeckPackList" });
            var packs=new SearchPicker { ItemsSource=vm.Data.Packs.Keys.Order().ToArray(),SelectedItem=row.Pack,Placeholder="搜索并选择 Pack" };editor.Children.Add(packs);
            packs.SelectedItemChanged+=(_,_)=> {if(packs.SelectedItem is string key && vm.Data.Packs.TryGetValue(key,out var p)){row.Pack=key;row.Unit=p.Unit;row.Transport=p.Transport;row.Xp=p.Xp;grid.Items.Refresh();} };
            editor.Children.Add(new ParameterNote { Text="DeckPackDescriptor.Unit" });
            var units=new SearchPicker { EnableUnitFilters=true,ItemsSource=vm.Data.Units.Units,DisplayMemberPath="DisplayName",SecondaryMemberPath="Name",SelectedItem=vm.Data.Units.Units.FirstOrDefault(u=>u.Name==row.Unit) };editor.Children.Add(units);
            units.SelectedItemChanged+=(_,_)=> {if(units.SelectedItem is WarnoLiteModdingTool.Core.Units.UnitRecord u){row.Unit=u.Name;grid.Items.Refresh();} };
            editor.Children.Add(new ParameterNote { Text="DeckPackDescriptor.Transport" });
            var transports=new SearchPicker { EnableUnitFilters=true,ItemsSource=vm.Data.Units.Units.Where(u=>u.HasUniqueTransporterModule).ToArray(),DisplayMemberPath="DisplayName",SecondaryMemberPath="Name",SelectedItem=vm.Data.Units.Units.FirstOrDefault(u=>u.Name==row.Transport),Placeholder="选择运输" };editor.Children.Add(transports);
            transports.SelectedItemChanged+=(_,_)=> {if(transports.SelectedItem is WarnoLiteModdingTool.Core.Units.UnitRecord u){row.Transport=u.Name;grid.Items.Refresh();} };
            var clear=new Button { Content=UiText.T("取消运输"),HorizontalAlignment=HorizontalAlignment.Left };clear.Click+=(_,_)=>{row.Transport="";transports.SelectedItem=null;grid.Items.Refresh();};editor.Children.Add(clear);
        };
        grid.SelectedIndex=rows.Count>0?0:-1;
        void Button(string text,Action action) { var b=new Button { Content=UiText.T(text),Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,8,0) };b.Click+=(_,_)=>action();actions.Children.Add(b); }
        Button("应用到草稿",()=> { try { if(!grid.CommitEdit(DataGridEditingUnit.Cell,true)||!grid.CommitEdit(DataGridEditingUnit.Row,true))return;if(original.SequenceEqual(rows.Select(r=>(r.Start,r.Count,r.Pack,r.Unit,r.Transport,r.Xp)))){DialogResult=true;return;}if(!Advanced.EditorMode.IsAdvanced){var cursor=0;foreach(var row in rows){row.Start=cursor;cursor+=row.Count;}} vm.ApplyMapping(rows);DialogResult=true; }catch(Exception ex){MessageBox.Show(this,ex.Message,"映射校验失败");} });
        if(Advanced.EditorMode.IsAdvanced) Button("自动重排索引",()=> { var cursor=0;foreach(var row in rows){row.Start=cursor;cursor+=row.Count;}grid.Items.Refresh(); });
        Button("取消",Close);
    }
}
