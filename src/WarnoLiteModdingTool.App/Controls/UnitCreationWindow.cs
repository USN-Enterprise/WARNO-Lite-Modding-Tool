using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Drafts;
using MessageBox=WarnoLiteModdingTool.App.Localisation.LocalizedMessageBox;
namespace WarnoLiteModdingTool.App.Controls;
public sealed class UnitCreationWindow:Window
{
    public DraftOperation? Result {get;private set;}
    private readonly UnitWorkspaceData _units;
    private readonly WeaponWorkspaceData _weapons;
    private readonly DivisionWorkspaceData? _divisions;
    private readonly IReadOnlyList<DraftOperation> _drafts;
    private readonly DraftOperation? _existing;
    private readonly UnitCreationState? _state;
    private UnitRecord? _mother;
    private readonly TextBox _name=new();
    private readonly CheckBox _independent=new();
    private readonly Dictionary<string,Control> _fields=[];
    private readonly List<DivisionRow> _rows=[];
    private readonly StackPanel _basics=new();
    private sealed record Choice(string Value,string Label);
    public sealed class DivisionRow:ObservableObject
    {
        public required DivisionRecord Division {get;init;}
        public string Label=>DivisionNames.Display(Division.DisplayName,UiText.Current.English);
        public bool Selected {get;set;}
        public bool WithoutTransport {get;set;}=true;
        public int Cards {get;set;}=1;
        public int Count {get;set;}=1;
        public string Xp {get;set;}="1, 1, 1, 1";
        public string[] Transports {get;set;}=[];
        public string TransportText=>string.Join(", ",Transports);
        public void Refresh(){foreach(var key in new[]{nameof(TransportText),nameof(Selected),nameof(WithoutTransport),nameof(Cards),nameof(Count),nameof(Xp)})OnPropertyChanged(key);}
    }
    public UnitCreationWindow(UnitWorkspaceData units,WeaponWorkspaceData weapons,DivisionWorkspaceData? divisions,IReadOnlyList<DraftOperation> drafts,DraftOperation? existing=null)
    {
        _units=units;_weapons=weapons;_divisions=divisions;_drafts=drafts;_existing=existing;_state=existing is null?null:UnitCreation.Read(existing);
        Title=UiText.T("新增单位");Width=950;Height=720;WindowStartupLocation=WindowStartupLocation.CenterOwner;SetResourceReference(BackgroundProperty,"SurfaceBrush");SetResourceReference(ForegroundProperty,"TextBrush");
        var root=new DockPanel{Margin=new Thickness(18)};Content=root;var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var tabs=new TabControl();root.Children.Add(tabs);
        StackPanel Page(string label){var panel=new StackPanel{Margin=new Thickness(14)};tabs.Items.Add(new TabItem{Header=UiText.T(label),Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});return panel;}
        var source=Page("1 选择母版");source.Children.Add(new TextBlock{Text=UiText.T("基于当前 Mod 的现有单位创建，沿用模型和动画。"),Margin=new Thickness(0,0,0,12)});
        var picker=new SearchPicker{ItemsSource=units.Units,DisplayMemberPath="DisplayName",SecondaryMemberPath="Name",EnableUnitFilters=true,IsEnabled=existing is null};source.Children.Add(picker);
        var basics=Page("2 基本设置");basics.Children.Add(_basics);
        var weapon=Page("3 武器配置");UiText.Bind(_independent,ContentControl.ContentProperty,"为新单位建立独立武器配置");_independent.IsChecked=_state?.IndependentWeapons??false;weapon.Children.Add(_independent);weapon.Children.Add(new TextBlock{Text=UiText.T("沿用母版的武器挂载；独立配置复制 Weapon，弹药保持共享，后续局部修改按需隔离。"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
        var divisionPage=Page("4 可用师");
        foreach(var d in divisions?.Divisions.Where(d=>d.CanEdit)??[])_rows.Add(new DivisionRow{Division=d});
        var divisionSearch=new TextBox{Margin=new Thickness(0,0,0,8),ToolTip=UiText.T("搜索名称或内部标识")};divisionPage.Children.Add(divisionSearch);
        var divisionView=new ListCollectionView(_rows){Filter=o=>o is DivisionRow row&&(row.Label.Contains(divisionSearch.Text,StringComparison.OrdinalIgnoreCase)||row.Division.Name.Contains(divisionSearch.Text,StringComparison.OrdinalIgnoreCase))};divisionSearch.TextChanged+=(_,_)=>divisionView.Refresh();
        var selectActions=new StackPanel{Orientation=Orientation.Horizontal};divisionPage.Children.Add(selectActions);foreach(var (label,selected) in new[]{("全选当前筛选",true),("取消全选",false)}){var action=new Button{Content=UiText.T(label),Margin=new Thickness(0,0,8,8)};action.Click+=(_,_)=>{foreach(DivisionRow row in divisionView){row.Selected=selected;row.Refresh();}};selectActions.Children.Add(action);}
        var grid=new DataGrid{ItemsSource=divisionView,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,IsReadOnly=false,Height=380,SelectionMode=DataGridSelectionMode.Single};divisionPage.Children.Add(grid);
        grid.Columns.Add(new DataGridCheckBoxColumn{Header=UiText.T("选择"),Binding=new Binding("Selected"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged}});
        grid.Columns.Add(new DataGridTextColumn{Header=UiText.T("师"),Binding=new Binding("Label"),IsReadOnly=true,Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        grid.Columns.Add(new DataGridCheckBoxColumn{Header=ParameterNote.Header("可无运输","AvailableWithoutTransport"),Binding=new Binding("WithoutTransport"){Mode=BindingMode.TwoWay}});
        foreach(var (label,path) in new[]{("最大卡数","Cards"),("单卡数量","Count"),("老练度倍率","Xp")})grid.Columns.Add(new DataGridTextColumn{Header=ParameterNote.Header(label,path switch {"Cards"=>"MaxPackNumber","Count"=>"NumberOfUnitInPack",_=>"NumberOfUnitInPackXPMultiplier"}),Binding=new Binding(path){Mode=BindingMode.TwoWay,ValidatesOnExceptions=true}});
        divisionPage.Children.Add(new ParameterNote {Text="AvailableTransportList"});var transport=new Button{Content=UiText.T("选择当前行运输"),HorizontalAlignment=HorizontalAlignment.Left};transport.Click+=(_,_)=>{if(grid.SelectedItem is not DivisionRow row)return;var choose=new UnitSelectionWindow(units.Units.Where(u=>u.HasUniqueTransporterModule).Select(u=>(u.Name,u.DisplayName)),row.Transports){Owner=this};if(choose.ShowDialog()==true){row.Transports=choose.SelectedIds.ToArray();row.Refresh();}};divisionPage.Children.Add(transport);
        var transportText=new TextBlock{TextWrapping=TextWrapping.Wrap};transportText.SetBinding(TextBlock.TextProperty,new Binding("SelectedItem.TransportText"){Source=grid});divisionPage.Children.Add(transportText);
        var review=Page("5 创建草稿");var summary=new TextBlock{TextWrapping=TextWrapping.Wrap,LineHeight=25};review.Children.Add(summary);
        tabs.SelectionChanged+=(_,_)=>{if(tabs.SelectedIndex==4)summary.Text=UiText.T("新增单位")+"："+_name.Text+"\n"+UiText.T("母版")+"："+_mother?.DisplayName+"\n"+UiText.T("所选师")+"："+_rows.Count(r=>r.Selected)+"\n"+UiText.T("加入草稿后可继续调整，正式文件只在应用草稿时写入。");};
        picker.SelectedItemChanged+=(_,_)=>{_mother=picker.SelectedItem as UnitRecord;BuildBasics();};picker.SelectedItem=units.Units.FirstOrDefault(u=>u.Name==_state?.Mother)??units.Units.FirstOrDefault();_mother=picker.SelectedItem as UnitRecord;BuildBasics();
        void Button(string label,Action action){var b=new Button{Content=UiText.T(label),Padding=new Thickness(14,8,14,8),Margin=new Thickness(8,12,0,0)};b.Click+=(_,_)=>action();actions.Children.Add(b);}
        Button("上一步",()=>tabs.SelectedIndex=Math.Max(0,tabs.SelectedIndex-1));Button("下一步",()=>tabs.SelectedIndex=Math.Min(4,tabs.SelectedIndex+1));
        Button("加入草稿",()=>{try{if(!grid.CommitEdit(DataGridEditingUnit.Cell,true)||!grid.CommitEdit(DataGridEditingUnit.Row,true))return;BuildResult();DialogResult=true;}catch(Exception ex){MessageBox.Show(this,ex.Message,"无法创建单位");}});Button("取消",Close);
    }
    private void BuildBasics()
    {
        if(_mother is null)return;_fields.Clear();_basics.Children.Clear();_name.Text=_state?.Name??_mother.DisplayName+" "+UiText.T("新单位");_basics.Children.Add(new TextBlock{Text=UiText.T("名称")});_basics.Children.Add(new ParameterNote {Text="NameToken → UNITS.csv.REFTEXT"});_basics.Children.Add(_name);
        foreach(var key in new[]{"structure.coalition","structure.country","structure.factory","structure.role","economy.commandPoints","survival.health"})
        {var field=_mother.Field(key);if(field?.CanEdit!=true)continue;_basics.Children.Add(new TextBlock{Text=UiText.T(field.Definition.Label),Margin=new Thickness(0,10,0,4)});_basics.Children.Add(new ParameterNote {Text=ParameterNote.ForUnit(field.Definition)});var value=_state?.Fields.GetValueOrDefault(key)??field.DisplayValue;Control input;if(field.Choices.Count>0){input=new ComboBox{ItemsSource=field.Choices.Select(c=>new Choice(c.Display,GameText.Display(key,c.Display))).ToArray(),DisplayMemberPath="Label",SelectedValuePath="Value",SelectedValue=value};}else input=new TextBox{Text=value};_fields[key]=input;_basics.Children.Add(input);}
        foreach(var row in _rows){row.Selected=false;row.WithoutTransport=true;row.Cards=1;row.Count=1;row.Transports=[];row.Xp="1, 1, 1, 1";var rule=_state?.Divisions.GetValueOrDefault(row.Division.Name)??row.Division.Baseline.UnitRules.FirstOrDefault(r=>r.Unit==_mother.Name);row.Selected=_state?.Divisions.ContainsKey(row.Division.Name)??false;if(rule is null){row.Refresh();continue;}row.WithoutTransport=rule.AvailableWithoutTransport;row.Cards=rule.MaxPackNumber;row.Count=rule.NumberOfUnitInPack;row.Transports=rule.AvailableTransports.ToArray();row.Xp=string.Join(", ",rule.XpMultipliers.Select(x=>x.ToString(System.Globalization.CultureInfo.InvariantCulture)));row.Refresh();}
    }
    private void BuildResult()
    {
        var mother=_mother??throw new InvalidDataException("请选择母版");
        var state=_state??UnitCreation.New(mother,_units,_drafts);
        var fields=new Dictionary<string,string>(_state?.Fields??[]);foreach(var (key,input) in _fields){var value=input is ComboBox combo?combo.SelectedValue?.ToString()??"":((TextBox)input).Text;if(value!=mother.Field(key)!.DisplayValue)fields[key]=value;else fields.Remove(key);}
        var divisionRules=new Dictionary<string,DivisionUnitRuleState>();var baselines=new Dictionary<string,string>();foreach(var row in _rows.Where(r=>r.Selected)){divisionRules[row.Division.Name]=new(state.Id,row.WithoutTransport,row.Transports,row.Cards,row.Count,row.Xp.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(v=>double.Parse(v,System.Globalization.CultureInfo.InvariantCulture)).ToArray());baselines[row.Division.Name]=_state?.DivisionBaselines.GetValueOrDefault(row.Division.Name)??DivisionDraftCodec.Serialize(row.Division.Baseline);}
        var weapons=new Dictionary<string,string>();foreach(var id in mother.Weapons){var w=_weapons.Weapon(id)??throw new InvalidDataException("母版武器不存在");weapons[id]=_state?.WeaponBaselines.GetValueOrDefault(id)??File.ReadAllText(w.Source.SourceFile).Substring(w.Source.CharacterOffset,w.Source.CharacterLength);}
        state=state with {Name=_name.Text.Trim(),Fields=fields,IndependentWeapons=_independent.IsChecked==true,Divisions=divisionRules,DivisionBaselines=baselines,WeaponBaselines=weapons};
        Result=UnitCreation.Operation(mother,state,_existing?.BaselineRaw);var resolved=UnitCreation.Resolve(_units,Result);if(resolved.Status!=DraftResolutionStatus.Active)throw new InvalidDataException(resolved.Reason);
        _=UnitCreation.Project(mother,state,Result.BaselineRaw);
    }
}
