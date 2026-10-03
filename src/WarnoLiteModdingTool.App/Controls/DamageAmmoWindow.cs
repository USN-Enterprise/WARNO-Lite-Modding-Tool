using System.IO;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Advanced;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Rules;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.Controls;

public sealed class DamageAmmoWindow : Window
{
    public sealed record DistanceRow(string Ammo,string Rule,string Before,string After,string Users);
    public sealed record StairChoice(string Label,DamageStair? Stair);
    private readonly DamageWorkspace _data;
    private readonly WeaponWorkspaceData _weapons;
    private readonly IReadOnlyList<AmmoRecord> _targets;
    private readonly DraftStore _store;
    private readonly DraftEditScope _scope;
    private readonly IReadOnlyList<string> _units;
    private readonly Action _changed;
    private readonly TextBox _distance=new(){Tag="damage-distance"},_ap=new(){Tag="damage-ap"};
    private readonly ComboBox _reference=new(){DisplayMemberPath="Label",Tag="damage-reference"};
    private readonly TextBlock _status=DamageUi.Text("");
    private readonly DataGrid _table=DamageUi.Table(("弹药","Ammo"),("原距离规则","Rule"),("原值（GRU / AP）","Before"),("草稿或预览（GRU / AP）","After"),("影响单位数","Users"));
    private DraftOperation[]? _preview;
    private bool _dirty,_loading,_conflict;
    public DamageAmmoWindow(DamageWorkspace data,WeaponWorkspaceData weapons,DraftStore store,IReadOnlyList<AmmoRecord> targets,
        DraftEditScope scope,IReadOnlyList<string> units,Action changed)
    {
        _data=data;_weapons=weapons;_store=store;_targets=targets;_scope=scope;_units=units;_changed=changed;
        DamageUi.Window(this,"距离规则与伤害查询");
        var body=new DockPanel{Margin=new(16)};Content=body;body.SetResourceReference(Panel.BackgroundProperty,"SurfaceBrush");
        var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);footer.Children.Add(DamageUi.Button("关闭",Close));
        var tabs=new TabControl();body.Children.Add(tabs);
        tabs.Items.Add(new TabItem{Header=UiText.T("距离规则"),Content=DistancePanel()});
        tabs.Items.Add(new TabItem{Header=UiText.T("伤害系数查询"),Content=QueryPanel()});
        Closing+=(_,e)=>{if(_dirty&&MessageBox.Show(this,UiText.T("有尚未加入草稿的输入，是否放弃并关闭？"),Title,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)e.Cancel=true;};
    }
    private string ScopeKey => _scope==DraftEditScope.AllReferences?"shared":string.Join(",",_units.Distinct().Order(StringComparer.Ordinal));
    private DraftOperation? Existing(AmmoRecord ammo)=>_store.Operations.SingleOrDefault(o=>o.TargetKind==DraftTargetKind.DamageDistance&&o.ObjectName==ammo.Name&&o.FieldKey=="distance/"+ScopeKey);
    private DamageStair? Original(AmmoRecord ammo)=>_data.ResolveStair(ammo.Source.RelativeSourceFile,ammo.Field(DamageDistance.ReferenceKey)?.RawValue??"");
    private UIElement DistancePanel()
    {
        var panel=new StackPanel{Margin=new(12)};
        panel.Children.Add(DamageUi.Heading("调整所选弹药的距离阶梯"));
        panel.Children.Add(DamageUi.Text(_scope==DraftEditScope.AllReferences?"弹药本体：影响所选弹药的全部使用者；其他弹药保留原阶梯。":"单位范围：仅所列单位使用修改后的弹药和阶梯，其他单位保持原值。",true));
        panel.Children.Add(DamageUi.Text("改变间隔时保持 Arme.Index 不变，远距离结果也可能变化；此处不自动补偿最大射程穿深。",true));
        _table.Height=180;panel.Children.Add(_table);
        var fields=new WrapPanel();panel.Children.Add(fields);fields.Children.Add(DamageUi.Field("距离间隔（GRU 原值）",_distance));
        fields.Children.Add(DamageUi.Field("每阶变化量（AP）",_ap));
        if(EditorMode.IsAdvanced)
        {
            var choices=new List<StairChoice>{new(UiText.T("保持现有引用，按范围独立调整"),null)};
            foreach(var stair in _data.Stairs)
            {
                try{foreach(var ammo in _targets)_data.Reference(ammo.Source.RelativeSourceFile,stair);choices.Add(new(stair.Source.Name,stair));}
                catch(Exception e)when(e is InvalidDataException or Core.Transactions.TransactionValidationException){ }
            }
            _reference.ItemsSource=choices;_reference.SelectedIndex=0;panel.Children.Add(DamageUi.Field("使用已有距离规则",_reference,660));
            panel.Children.Add(DamageUi.Text("批量时 AP 留空会保留各自的当前值。选择已有规则会直接引用其当前参数；需要修改共享本体请到游戏规则页。",true));
            _reference.SelectionChanged+=(_,_)=>{_distance.IsEnabled=_ap.IsEnabled=(_reference.SelectedItem as StairChoice)?.Stair is null;Invalidate();};
        }
        else
        {
            _ap.IsReadOnly=true;panel.Children.Add(DamageUi.Text("普通模式仅调整已识别动能穿甲弹的间隔；每阶变化量保持不变。其他家族和引用切换在专业模式处理。",true));
        }
        panel.Children.Add(_status);
        var actions=new WrapPanel();panel.Children.Add(actions);
        actions.Children.Add(DamageUi.Button("计算预览",Preview,"damage-distance-preview"));
        actions.Children.Add(DamageUi.AsyncButton("加入草稿",Save,e=>_status.Text=UiText.T(e.Message),"damage-distance-save"));
        actions.Children.Add(DamageUi.AsyncButton("撤销此范围距离草稿",async()=>
        {
            await _store.ApplyBatchAsync([], _targets.Select(Existing).OfType<DraftOperation>().Select(o=>o.Id).ToArray());
            _changed();_dirty=false;Load();_status.Text=UiText.T("已撤销草稿");
        },e=>_status.Text=UiText.T(e.Message),"damage-distance-undo"));
        fields.IsEnabled=actions.IsEnabled=!_store.IsBlocked;
        _distance.TextChanged+=(_,_)=>Invalidate();_ap.TextChanged+=(_,_)=>Invalidate();
        var refs=_targets.SelectMany(a=>(_scope==DraftEditScope.AllReferences?_weapons.References.AmmoUnits.GetValueOrDefault(a.Name,[]):_units)
            .Select(u=>a.Name+" · "+(_weapons.Units.FirstOrDefault(x=>x.Name==u)?.DisplayName??u)+" · "+u)).Distinct().ToArray();
        panel.Children.Add(new Expander{Header=UiText.T("完整单位影响范围")+$" · {refs.Length}",Content=new ListBox{ItemsSource=refs,MaxHeight=200,MinHeight=100},Margin=new(0,12,0,0)});
        var graphRefs=_targets.SelectMany(a=>_data.References(a.Name)).Distinct().ToArray();
        panel.Children.Add(new Expander{Header=UiText.T("所选弹药的直接引用（基线）"),Content=new ListBox{ItemsSource=graphRefs,MaxHeight=180,MinHeight=90},Margin=new(0,8,0,0)});
        Load();return DamageUi.Scroll(panel);
    }
    private void Invalidate(){if(_loading)return;_preview=null;_dirty=true;_status.Text=UiText.T("输入已变化，请重新计算预览。");}
    private void Load()
    {
        _loading=true;_preview=null;_conflict=false;if(EditorMode.IsAdvanced)_reference.SelectedIndex=0;
        try
        {
            var rows=new List<DistanceRow>();var distances=new List<string>();var aps=new List<string>();
            foreach(var a in _targets)
            {
                var stair=Original(a);var existing=Existing(a);var d=stair?.Distance??"";var ap=stair?.AP??"";
                if(existing is not null)
                {
                    DamageDistance.Validate(_data,_weapons,existing);var state=DamageDistance.Read(existing);d=state.Distance;ap=state.AP;
                    if(_targets.Count==1 && state.ChangeReference && EditorMode.IsAdvanced)
                        _reference.SelectedItem=_reference.Items.OfType<StairChoice>().FirstOrDefault(c=>c.Stair?.Source.Name==state.StairName);
                }
                distances.Add(d);aps.Add(ap);rows.Add(new(a.DisplayName,stair?.Source.Name??a.Field(DamageDistance.ReferenceKey)?.RawValue??"—",
                    stair is null?UiText.T("无法识别现有阶梯"):stair.Distance+" / "+stair.AP,d+" / "+ap,(_scope==DraftEditScope.AllReferences?_weapons.References.AmmoUnits.GetValueOrDefault(a.Name,[]).Count:_units.Count).ToString()));
            }
            _distance.Text=distances.Distinct().Count()==1?distances.First():"";_ap.Text=aps.Distinct().Count()==1?aps.First():"";_table.ItemsSource=rows;
            _status.Text=_targets.Count==0?UiText.T("请先选择弹药"):!EditorMode.IsAdvanced && _targets.Any(a=>!DamageDistance.Basic(a)||Original(a) is null)?UiText.T("所选范围含普通模式不支持的弹药；请缩小选择或使用专业模式。"):"";
        }
        catch(Exception e){_conflict=true;_status.Text=UiText.T(e.Message);}
        finally{_loading=false;}
    }
    private void Preview()
    {
        try
        {
            if(_store.IsBlocked||_conflict)throw new InvalidDataException("当前状态不能编辑，请处理草稿冲突或切换专业模式。");
            if(_targets.Count==0)throw new InvalidDataException("请先选择弹药");
            var reference=EditorMode.IsAdvanced?(_reference.SelectedItem as StairChoice)?.Stair:null;
            var ops=new List<DraftOperation>();
            foreach(var a in _targets)
            {
                if(!EditorMode.IsAdvanced && !DamageDistance.Basic(a))throw new InvalidDataException(UiText.T("普通模式不支持此弹药")+": "+a.Name);
                var stair=reference??Original(a)??throw new InvalidDataException(UiText.T("无法识别现有阶梯")+": "+a.Name);
                var existing=Existing(a);var prior=existing is null?null:DamageDistance.Read(existing);
                if(!EditorMode.IsAdvanced && prior?.ChangeReference==true)throw new InvalidDataException("此范围已有专业引用草稿，请先应用、撤销或在专业模式处理。");
                var ap=reference?.AP??(EditorMode.IsAdvanced && _ap.Text.Trim().Length>0?_ap.Text.Trim():prior?.AP??stair.AP);
                var op=DamageDistance.Operation(_data,a,stair,reference?.Distance??_distance.Text.Trim(),ap,_scope,_units,reference is not null);
                DamageDistance.Validate(_data,_weapons,op);ops.Add(op);
            }
            _preview=ops.ToArray();_dirty=true;
            _table.ItemsSource=_targets.Select((a,i)=>{var s=DamageDistance.Read(ops[i]);var original=Original(a);return new DistanceRow(a.DisplayName,s.StairName,original is null?"—":original.Distance+" / "+original.AP,s.Distance+" / "+s.AP,
                (_scope==DraftEditScope.AllReferences?_weapons.References.AmmoUnits.GetValueOrDefault(a.Name,[]).Count:_units.Count).ToString());}).ToArray();
            _status.Text=UiText.T("预览已完成；确认目标和影响范围后加入草稿。")+" · "+ops.Count;
        }
        catch(Exception e){_preview=null;_status.Text=UiText.T(e.Message);}
    }
    private async Task Save()
    {
        if(_preview is null)throw new InvalidDataException("请先计算预览");
        var upserts=new List<DraftOperation>();var removals=new List<string>();
        foreach(var op in _preview)
        {
            var state=DamageDistance.Read(op);var original=Original(_targets.Single(a=>a.Name==op.ObjectName));
            if(original is not null && original.Source.Name==state.StairName && DamageWorkspace.Number(original.Distance)==DamageWorkspace.Number(state.Distance)&&DamageWorkspace.Number(original.AP)==DamageWorkspace.Number(state.AP))removals.Add(op.Id);
            else upserts.Add(op);
        }
        await _store.ApplyBatchAsync(upserts,removals);_dirty=false;_changed();Load();_status.Text=UiText.T("草稿已保存");
    }
    private UIElement QueryPanel()
    {
        var panel=new StackPanel{Margin=new(12)};panel.Children.Add(DamageUi.Heading("弹药对目标单位的静态系数"));
        panel.Children.Add(DamageUi.Text("按当前正式文件查询，不包含未应用草稿。系数不是最终伤害，不计距离、命中、压制映射或掩护带来的抗性切换。",true));
        var ammo=new SearchPicker{ItemsSource=_targets,DisplayMemberPath="DisplayName",SecondaryMemberPath="Name",Tag="damage-query-ammo"};
        var unit=new SearchPicker{ItemsSource=_weapons.Units,DisplayMemberPath="DisplayName",SecondaryMemberPath="Name",Tag="damage-query-unit"};
        var side=new ComboBox{ItemsSource=new[]{UiText.T("正面"),UiText.T("侧面"),UiText.T("后方"),UiText.T("顶部")},SelectedIndex=0,Tag="damage-query-side"};
        panel.Children.Add(DamageUi.Field("攻击弹药",ammo,660));panel.Children.Add(DamageUi.Field("目标单位",unit,660));panel.Children.Add(DamageUi.Field("目标部位",side));
        var result=DamageUi.Text("");result.SetResourceReference(StyleProperty,"RuleObjectHeading");panel.Children.Add(result);
        var details=DamageUi.Text("",true);panel.Children.Add(details);
        void Query()
        {
            if(ammo.SelectedItem is not AmmoRecord a || unit.SelectedItem is not UnitRecord u)return;
            var key="armor."+new[]{"front","side","rear","top"}[side.SelectedIndex];
            var family=a.Field("ammo.damage.family")?.DisplayValue??"";var index=a.Field("ammo.damage.index")?.DisplayValue??"";
            var resistance=u.Field(key+".family")?.DisplayValue??"";var armor=u.Field(key)?.DisplayValue??"";
            var cell=_data.Matrix?.Cells.SingleOrDefault(c=>c.Attack.Family==family&&c.Attack.Index.ToString()==index&&c.Resistance.Family==resistance&&c.Resistance.Index.ToString()==armor);
            result.Text=cell is null?UiText.T("当前组合无法查询")+" · "+_data.MatrixError:UiText.T("矩阵系数")+" = "+cell.Raw;
            details.Text=$"{family} / {index} → {resistance} / {armor}\nArme.Index · {_data.Matrix?.File}";
        }
        ammo.SelectedItemChanged+=(_,_)=>Query();unit.SelectedItemChanged+=(_,_)=>Query();side.SelectionChanged+=(_,_)=>Query();
        ammo.SelectedItem=_targets.FirstOrDefault();unit.SelectedItem=_weapons.Units.FirstOrDefault();return DamageUi.Scroll(panel);
    }
}
