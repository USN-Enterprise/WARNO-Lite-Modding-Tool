using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Rules;
namespace WarnoLiteModdingTool.App.ViewModels;
public sealed class RulesWorkspaceViewModel
{
    public RulesWorkspaceViewModel(RuleWorkspace data,DraftStore store,Action changed)
    {
        Groups = data.Groups.Select(g => new RuleGroupViewModel(data,g,store,changed)).ToArray();
        View = CollectionViewSource.GetDefaultView(Groups); View.Filter = o => o is RuleGroupViewModel g && (Advanced.EditorMode.IsAdvanced || g.Group.Definition.Basic) && (Search.Length == 0 || g.Title.Contains(Search,StringComparison.OrdinalIgnoreCase) || g.Group.Definition.Fields.Contains(Search,StringComparison.OrdinalIgnoreCase));
    }
    public IReadOnlyList<RuleGroupViewModel> Groups {get;}
    public ICollectionView View {get;}
    public string Search {get;set;} = "";
    public void Refresh() => View.Refresh();
    public void Restore() {foreach(var group in Groups)group.Restore();}
    public async Task FlushAsync() { foreach(var group in Groups) { await group.SaveAsync(); if(group.HasPendingError)throw new InvalidOperationException(group.Title + "：" + group.Status); } }
}
public sealed class RuleGroupViewModel : ObservableObject
{
    private readonly RuleWorkspace _workspace;
    private readonly DraftStore _store;
    private readonly Action _changed;
    private readonly DispatcherTimer _timer = new() {Interval=TimeSpan.FromMilliseconds(350)};
    private readonly SemaphoreSlim _gate = new(1,1);
    private bool _dirty;
    private string _status = "";
    public RuleGroupViewModel(RuleWorkspace workspace,RuleGroup group,DraftStore store,Action changed)
    {
        _workspace=workspace;Group=group;_store=store;_changed=changed;
        var draft = store.Operations.FirstOrDefault(o => o.TargetKind == DraftTargetKind.GlobalRule && o.FieldKey == group.Definition.Number.ToString());
        Dictionary<string,string>? values = null;
        if(draft is not null && workspace.Resolve(draft).Status == DraftResolutionStatus.Active) values=RuleWorkspace.Values(draft);
        Cells = group.Cells.Select(c => new RuleCellViewModel(c,values?.GetValueOrDefault(c.Key) ?? c.Raw,MarkDirty)).ToArray();
        Status = group.Error.Length > 0 ? group.Error : draft is null ? "" : workspace.Resolve(draft).Reason.Length > 0 ? workspace.Resolve(draft).Reason : "草稿已保存";
        _timer.Tick += async (_,_) => { _timer.Stop(); await SaveAsync(); };
    }
    public RuleGroup Group {get;}
    public string Title => Group.Definition.Number + ". " + Localisation.UiText.T(Group.Definition.Label);
    public IReadOnlyList<RuleCellViewModel> Cells {get;}
    public bool CanEdit => Group.CanEdit && !_store.IsBlocked;
    public string Status {get=>_status;private set=>SetProperty(ref _status,value);}
    public bool HasPendingError => _dirty;
    public void Restore()
    {
        _timer.Stop();
        var draft=_store.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.GlobalRule&&o.FieldKey==Group.Definition.Number.ToString());
        var values=draft is not null && _workspace.Resolve(draft).Status==DraftResolutionStatus.Active ? RuleWorkspace.Values(draft) : null;
        foreach(var c in Cells)c.Value=values?.GetValueOrDefault(c.Cell.Key)??c.Cell.Raw;
        _timer.Stop();_dirty=false;Status=Group.Error.Length>0?Group.Error:draft is null?"":"草稿已恢复";
    }
    private void MarkDirty(){_dirty=true;_timer.Stop();_timer.Start();}
    public async Task SaveAsync()
    {
        _timer.Stop(); await _gate.WaitAsync();
        try
        {
            if(!_dirty)return;
            var values=Cells.ToDictionary(c=>c.Cell.Key,c=>c.Value.Trim());
            var operation=_workspace.Operation(Group,values);
            if(Cells.All(c=>c.Cell.Raw == values[c.Cell.Key])) await _store.RemoveAsync(operation.Id);
            else await _store.UpsertAsync(operation);
            _dirty=false;Status="草稿已保存";_changed();
        }
        catch(Exception ex){Status=ex.Message;}
        finally{_gate.Release();}
    }
    public async Task UndoAsync()
    {
        _timer.Stop();await _gate.WaitAsync();
        try{foreach(var c in Cells)c.Value=c.Cell.Raw;_timer.Stop();_dirty=false;
            var op=_store.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.GlobalRule&&o.FieldKey==Group.Definition.Number.ToString());
            if(op is not null)await _store.RemoveAsync(op.Id);Status="已撤销草稿";_changed();}
        finally{_gate.Release();}
    }
}
public sealed class RuleCellViewModel(RuleCell cell,string initial,Action changed) : ObservableObject
{
    private string _value=initial;
    public RuleCell Cell {get;}=cell;
    public string Value {get=>_value;set{if(SetProperty(ref _value,value))changed();}}
}
