using System.IO;
using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Advanced;
using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Rules;

namespace WarnoLiteModdingTool.App.Controls;

public sealed class DamageRulesWindow : Window
{
    public sealed record CellRow(DamageCell Cell,string Baseline,string Current,string Target)
    {
        public int Attack => Cell.Attack.Index;
        public int Resistance => Cell.Resistance.Index;
    }
    private readonly DamageWorkspace _data;
    private readonly DraftStore _store;
    private readonly Action _changed;
    private readonly DataGrid _table=DamageUi.Table(("攻击档位","Attack"),("抗性档位","Resistance"),("原值","Baseline"),("草稿值","Current"),("预览值","Target"));
    private readonly ComboBox _attack=new(){Tag="damage-family"},_resistance=new(){Tag="resistance-family"};
    private readonly TextBox _attackFrom=new(){Tag="damage-attack-from"},_attackTo=new(){Tag="damage-attack-to"},_resistanceFrom=new(){Tag="damage-resistance-from"},_resistanceTo=new(){Tag="damage-resistance-to"};
    private readonly ComboBox _math=new(){ItemsSource=new[]{UiText.T("设为"),UiText.T("乘以"),UiText.T("增加")},SelectedIndex=0,Tag="damage-math"};
    private readonly TextBox _operand=new(){Text="1",Tag="damage-operand"},_min=new(),_max=new();
    private readonly CheckBox _zero=new(){Content=UiText.T("保留零值"),IsChecked=true,VerticalAlignment=VerticalAlignment.Center,Margin=new(0,8,12,8)};
    private readonly TextBlock _status=DamageUi.Text(""),_selection=DamageUi.Text(""),_stairStatus=DamageUi.Text("");
    private readonly ListBox _impact=new(){MinHeight=100,MaxHeight=180};
    private readonly ComboBox _stair=new(){DisplayMemberPath="Source.Name",Tag="damage-shared-stair"};
    private readonly TextBox _distance=new(){Tag="damage-shared-distance"},_ap=new(){Tag="damage-shared-ap"};
    private IReadOnlyList<DamageChange>? _preview;
    private bool _matrixDirty,_stairDirty,_loading,_matrixConflict;
    public DamageRulesWindow(DamageWorkspace data,DraftStore store,Action changed)
    {
        _data=data;_store=store;_changed=changed; DamageUi.Window(this,"伤害与抗性规则");
        var body=new DockPanel{Margin=new(16)};Content=body;body.SetResourceReference(Panel.BackgroundProperty,"SurfaceBrush");
        var note=DamageUi.Text("共享规则作用于所有匹配对象，包括双方单位。这里只保存草稿，正式应用与备份在草稿中心完成。",true);
        DockPanel.SetDock(note,Dock.Top);body.Children.Add(note);
        var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);
        footer.Children.Add(DamageUi.Button("关闭",Close));
        var tabs=new TabControl();body.Children.Add(tabs);
        tabs.Items.Add(new TabItem{Header=UiText.T("伤害与抗性矩阵"),Content=MatrixPanel()});
        tabs.Items.Add(new TabItem{Header=UiText.T("共享距离规则"),Content=StairPanel()});
        if(data.Diagnostics.Count>0) tabs.Items.Add(new TabItem{Header=UiText.T("诊断"),Content=new ListBox{ItemsSource=data.Diagnostics}});
        Closing+=(_,e)=>{if((_matrixDirty || _stairDirty) && MessageBox.Show(this,UiText.T("有尚未加入草稿的输入，是否放弃并关闭？"),Title,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)e.Cancel=true;};
    }
    private UIElement MatrixPanel()
    {
        var panel=new DockPanel{Margin=new(12)};
        if(_data.Matrix is not {} matrix){panel.Children.Add(DamageUi.Text(_data.MatrixError));return panel;}
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
        top.Children.Add(DamageUi.Heading("按伤害家族和抗性家族选择单元"));
        top.Children.Add(DamageUi.Text("系数允许大于 1；0 表示此矩阵项为零。未知表达式只读。按住 Ctrl 或 Shift 可多选，批量计算从当前草稿值继续。",true));
        var filters=new WrapPanel();top.Children.Add(filters);
        filters.Children.Add(DamageUi.Field("伤害家族",FamilyRange(_attack,_attackFrom,_attackTo),330));filters.Children.Add(DamageUi.Field("抗性家族",FamilyRange(_resistance,_resistanceFrom,_resistanceTo),330));
        _attack.ItemsSource=matrix.Rows.Select(r=>r.Family).Distinct().ToArray();_resistance.ItemsSource=matrix.Columns.Select(r=>r.Family).Distinct().ToArray();
        var values=new WrapPanel();top.Children.Add(values);
        values.Children.Add(DamageUi.Field("计算方式",_math,160));values.Children.Add(DamageUi.Field("数值",_operand,130));
        values.Children.Add(DamageUi.Field("下限（可选）",_min,140));values.Children.Add(DamageUi.Field("上限（可选）",_max,140));values.Children.Add(_zero);
        var actions=new WrapPanel();top.Children.Add(actions);
        actions.Children.Add(DamageUi.Button("全选当前表格",()=>_table.SelectAll(),"damage-select-all"));
        actions.Children.Add(DamageUi.Button("计算预览",Preview,"damage-preview"));
        actions.Children.Add(DamageUi.AsyncButton("加入草稿",SaveMatrix,e=>_status.Text=UiText.T(e.Message),"damage-save"));
        actions.Children.Add(DamageUi.AsyncButton("撤销所选单元草稿",UndoCells,e=>_status.Text=UiText.T(e.Message)));
        top.Children.Add(_selection);top.Children.Add(_status);
        var bottom=new Expander{Header=UiText.T("涉及的家族引用（含间接与全局定义）"),Content=_impact,Margin=new(0,8,0,0)};
        DockPanel.SetDock(bottom,Dock.Bottom);panel.Children.Add(bottom);panel.Children.Add(_table);
        _attack.SelectionChanged+=(_,_)=>LoadCells();_resistance.SelectionChanged+=(_,_)=>LoadCells();
        foreach(var range in new[]{_attackFrom,_attackTo,_resistanceFrom,_resistanceTo})range.TextChanged+=(_,_)=>LoadCells();
        _table.SelectionChanged+=(_,_)=>{_preview=null;Count();};
        void Invalidate(){if(_loading)return;_preview=null;_matrixDirty=true;_status.Text=UiText.T("输入已变化，请重新计算预览。");}
        _operand.TextChanged+=(_,_)=>Invalidate();_min.TextChanged+=(_,_)=>Invalidate();_max.TextChanged+=(_,_)=>Invalidate();_math.SelectionChanged+=(_,_)=>Invalidate();
        _zero.Checked+=(_,_)=>Invalidate();_zero.Unchecked+=(_,_)=>Invalidate();
        _attack.SelectedIndex=0;_resistance.SelectedIndex=0;
        values.IsEnabled=actions.IsEnabled=EditorMode.IsAdvanced && !_store.IsBlocked;
        return panel;
    }
    private static StackPanel FamilyRange(ComboBox family,TextBox from,TextBox to)
    {
        var panel=new StackPanel();panel.Children.Add(family);var range=new WrapPanel{Margin=new(0,5,0,0)};panel.Children.Add(range);
        var label=DamageUi.Text("档位范围");label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new(0,0,8,0);range.Children.Add(label);
        foreach(var input in new[]{from,to}){input.Width=76;input.ToolTip=UiText.T("留空表示本家族全部档位");}
        range.Children.Add(from);range.Children.Add(new TextBlock{Text="–",Margin=new(6,0,6,0),VerticalAlignment=VerticalAlignment.Center});range.Children.Add(to);return panel;
    }
    private static (int From,int To) Range(TextBox from,TextBox to,int max)
    {
        var first=from.Text.Trim().Length==0?1:int.TryParse(from.Text,out var f)?f:0;
        var last=to.Text.Trim().Length==0?max:int.TryParse(to.Text,out var t)?t:0;
        if(first<1||last<first||last>max)throw new InvalidDataException("档位范围必须在当前家族边界内");return(first,last);
    }
    private Dictionary<string,string> Current()
    {
        var op=_store.Operations.SingleOrDefault(o=>o.TargetKind==DraftTargetKind.DamageRule&&o.FieldKey=="matrix");
        if(op is null)return [];
        _data.Validate(op);return DamageWorkspace.Read(op).Changes.ToDictionary(c=>c.Key,c=>c.After);
    }
    private void Count()=>_selection.Text=UiText.T("已选单元")+$" {_table.SelectedItems.Count} / {_table.Items.Count}";
    private void LoadCells()
    {
        if(_attack.SelectedItem is not string attack || _resistance.SelectedItem is not string resistance)return;
        _preview=null;_matrixConflict=false;
        Dictionary<string,string> current=[];
        try{current=Current();_status.Text="";}catch(Exception e){_matrixConflict=true;_status.Text=UiText.T(e.Message);}
        try
        {
            var matrix=_data.Matrix!;var a=Range(_attackFrom,_attackTo,matrix.Rows.Where(r=>r.Family==attack).Max(r=>r.Index));
            var r=Range(_resistanceFrom,_resistanceTo,matrix.Columns.Where(r=>r.Family==resistance).Max(r=>r.Index));
            _table.ItemsSource=matrix.Cells.Where(c=>c.Attack.Family==attack&&c.Resistance.Family==resistance&&c.Attack.Index>=a.From&&c.Attack.Index<=a.To&&c.Resistance.Index>=r.From&&c.Resistance.Index<=r.To)
                .Select(c=>new CellRow(c,c.Raw,current.GetValueOrDefault(c.Key,c.Raw),"—")).ToArray();
        }
        catch(InvalidDataException e){_table.ItemsSource=Array.Empty<CellRow>();_status.Text=UiText.T(e.Message);}
        _impact.ItemsSource=_data.FullReferences(attack,resistance);Count();
    }
    private void Editable(bool matrix=false){if(!EditorMode.IsAdvanced||_store.IsBlocked||matrix&&_matrixConflict)throw new InvalidDataException("当前状态不能编辑，请处理草稿冲突或切换专业模式。");}
    private void Preview()
    {
        try
        {
            Editable(true);var rows=_table.SelectedItems.Cast<CellRow>().ToArray();if(rows.Length==0)throw new InvalidDataException("请先选择单元");
            var preview=DamageMatrixBatch.Preview(rows.Select(r=>r.Cell),Current(),(DamageMath)_math.SelectedIndex,_operand.Text,_min.Text,_max.Text,_zero.IsChecked==true);
            var targets=preview.ToDictionary(c=>c.Key,c=>c.After);var selected=rows.Select(r=>r.Cell.Key).ToHashSet();
            var display=_table.Items.Cast<CellRow>().Select(r=>r with{Target=targets.GetValueOrDefault(r.Cell.Key,"—")}).ToArray();
            _table.ItemsSource=display;foreach(var row in display.Where(r=>selected.Contains(r.Cell.Key)))_table.SelectedItems.Add(row);
            _preview=preview;_matrixDirty=true;
            var changed=rows.Count(r=>DamageWorkspace.Number(r.Current)!=DamageWorkspace.Number(targets[r.Cell.Key]));
            _status.Text=UiText.T("预览变化")+$" {changed} · "+UiText.T("零值变为非零")+$" {rows.Count(r=>DamageWorkspace.Number(r.Current)==0&&DamageWorkspace.Number(targets[r.Cell.Key])!=0)} · "+UiText.T("非零变为零")+$" {rows.Count(r=>DamageWorkspace.Number(r.Current)!=0&&DamageWorkspace.Number(targets[r.Cell.Key])==0)}";
        }
        catch(Exception e){_preview=null;_status.Text=UiText.T(e.Message);}
    }
    private async Task SaveMatrix()
    {
        Editable(true);if(_preview is null)throw new InvalidDataException("请先计算预览");
        var merged=Current();foreach(var c in _preview)merged[c.Key]=c.After;
        await SaveCells(merged);_matrixDirty=false;LoadCells();_status.Text=UiText.T("草稿已保存");
    }
    private async Task UndoCells()
    {
        Editable(true);var current=Current();foreach(var r in _table.SelectedItems.Cast<CellRow>())current.Remove(r.Cell.Key);
        await SaveCells(current);_matrixDirty=false;LoadCells();_status.Text=UiText.T("已撤销草稿");
    }
    private async Task SaveCells(Dictionary<string,string> values)
    {
        var cells=_data.Matrix!.Cells.ToDictionary(c=>c.Key);var changes=values.Where(v=>v.Value!=cells[v.Key].Raw).Select(v=>new DamageChange(v.Key,cells[v.Key].Raw,v.Value)).ToArray();
        if(changes.Length>0)await _store.UpsertAsync(_data.MatrixOperation(changes));
        else if(_store.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.DamageRule&&o.FieldKey=="matrix") is {} op)await _store.RemoveAsync(op.Id);
        _changed();
    }
    private UIElement StairPanel()
    {
        var panel=new StackPanel{Margin=new(12)};
        panel.Children.Add(DamageUi.Heading("编辑共享距离阶梯"));
        panel.Children.Add(DamageUi.Text("修改本体会影响下列全部引用。仅调整某些弹药或单位，请从弹药页或武器页进入距离编辑器。",true));
        panel.Children.Add(_stair);_stair.ItemsSource=_data.Stairs;
        var fields=new WrapPanel();fields.Children.Add(DamageUi.Field("距离间隔（GRU 原值）",_distance));fields.Children.Add(DamageUi.Field("每阶变化量（AP）",_ap));panel.Children.Add(fields);
        panel.Children.Add(DamageUi.Text("改变间隔时保持 Arme.Index 不变，远距离结果也可能变化；此处不自动补偿最大射程穿深。",true));
        panel.Children.Add(_stairStatus);
        var refs=new ListBox{MinHeight=160,MaxHeight=300};panel.Children.Add(DamageUi.Heading("完整引用链（含全局定义）"));panel.Children.Add(refs);
        var actions=new WrapPanel();panel.Children.Add(actions);
        actions.Children.Add(DamageUi.AsyncButton("加入草稿",async()=>
        {
            Editable();var stair=(DamageStair?)_stair.SelectedItem??throw new InvalidDataException("请选择距离规则");
            var op=_data.StairOperation(stair,_distance.Text.Trim(),_ap.Text.Trim());
            if(DamageWorkspace.Read(op).Changes.All(c=>DamageWorkspace.Number(c.Before)==DamageWorkspace.Number(c.After)))await _store.RemoveAsync(op.Id);
            else await _store.UpsertAsync(op);
            _stairDirty=false;_changed();_stairStatus.Text=UiText.T("草稿已保存");
        },e=>_stairStatus.Text=UiText.T(e.Message),"damage-shared-save"));
        actions.Children.Add(DamageUi.AsyncButton("撤销此项",async()=>
        {
            if(_stair.SelectedItem is not DamageStair stair)return;
            var op=_store.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.DamageRule&&o.FieldKey=="stair"&&o.ObjectName==stair.Source.Name);
            if(op is not null)await _store.RemoveAsync(op.Id);_stairDirty=false;_changed();LoadStair();
        },e=>_stairStatus.Text=UiText.T(e.Message)));
        void LoadStair()
        {
            if(_stair.SelectedItem is not DamageStair stair)return;_loading=true;
            try
            {
                _distance.Text=stair.Distance;_ap.Text=stair.AP;_stairStatus.Text=stair.Source.RelativeSourceFile;
                refs.ItemsSource=_data.FullReferences(stair.Source.Name);
                var op=_store.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.DamageRule&&o.FieldKey=="stair"&&o.ObjectName==stair.Source.Name);
                fields.IsEnabled=actions.IsEnabled=EditorMode.IsAdvanced&&!_store.IsBlocked;
                if(op is not null){_data.Validate(op);var values=DamageWorkspace.Read(op).Changes.ToDictionary(c=>c.Key,c=>c.After);_distance.Text=values["DistanceGRU"];_ap.Text=values["AP"];}
            }
            catch(Exception e){fields.IsEnabled=false;actions.IsEnabled=false;_stairStatus.Text=UiText.T(e.Message);}
            finally{_loading=false;}
        }
        _stair.SelectionChanged+=(_,e)=>
        {
            if(_loading)return;
            if(_stairDirty && MessageBox.Show(this,UiText.T("当前阶梯有未保存输入，是否放弃并切换？"),Title,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)
            { _loading=true;_stair.SelectedItem=e.RemovedItems.Count>0?e.RemovedItems[0]:null;_loading=false;return; }
            _stairDirty=false;LoadStair();
        };_distance.TextChanged+=(_,_)=>{if(!_loading)_stairDirty=true;};_ap.TextChanged+=(_,_)=>{if(!_loading)_stairDirty=true;};
        _stair.SelectedIndex=_data.Stairs.Count>0?0:-1;return DamageUi.Scroll(panel);
    }
}
