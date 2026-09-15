using System.Collections.ObjectModel;
using System.Windows.Data;
using System.ComponentModel;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Strategic;
namespace WarnoLiteModdingTool.App.ViewModels;
public sealed record PackListItem(StrategicPack Source,StrategicPackEdit State,string Country,string Coalition,string Status,string UnitDisplay,string TransportDisplay)
{
    public string Id=>Source.Source.Info.Name;
    public string Name=>State.Name;
    public string Unit=>State.Unit;
    public string Transport=>State.Transport;
    public int Xp=>State.Xp;
}
public sealed class StrategicPackWorkspaceViewModel : ObservableObject
{
    private readonly DraftStore _store;private readonly Action _changed;private PackListItem? _selected;private string _search="";
    public StrategicPackWorkspaceViewModel(StrategicWorkspace data,DraftStore store,Action changed){Data=data;_store=store;_changed=changed;View=new ListCollectionView(Items);View.Filter=o=>o is PackListItem p&&(Filter?.Invoke(p.Id)??true)&&(p.Name+" "+p.Unit+" "+UnitName(p.Unit)+" "+p.Transport+" "+p.TransportDisplay).Contains(Search,StringComparison.OrdinalIgnoreCase);Refresh();}
    public Dictionary<string,StrategicPackEdit> PendingEdits {get;} = new();
    public StrategicWorkspace Data {get;}
    public ObservableCollection<PackListItem> Items {get;}=[];
    public ICollectionView View {get;}
    public Func<string,bool>? Filter {get;set;}
    public string Search {get=>_search;set{if(SetProperty(ref _search,value))View.Refresh();}}
    public bool Locked {get;set;}
    public bool CanEdit=>!Locked&&!_store.IsBlocked;
    public string UnitName(string name)=>Data.Units.Units.FirstOrDefault(u=>u.Name==name)?.DisplayName??name;
    public PackListItem? Selected {get=>_selected;set{if(SetProperty(ref _selected,value)){OnPropertyChanged(nameof(Uses));OnPropertyChanged(nameof(Impact));}}}
    public IReadOnlyList<StrategicPackUse> Uses {get {try{return Selected is null?[]:StrategicPackEditing.Uses(Data,Selected.Id);}catch(System.IO.InvalidDataException){return [];}}}
    public string Impact {get {try{var uses=Selected is null?[]:StrategicPackEditing.Uses(Data,Selected.Id);return (Data.HasRoster?"":Localisation.UiText.T("编制资料不完整，统计仅限已识别文件")+" · ")+Localisation.UiText.T("共享本体修改影响全部引用")+$" · Deck {uses.Select(u=>u.Deck).Distinct().Count()} · "+Localisation.UiText.T("槽位")+$" {uses.Select(u=>(u.Deck,u.Slot)).Distinct().Count()} · "+Localisation.UiText.T("营团")+$" {uses.Where(u=>u.PawnId.Length>0).Select(u=>u.PawnId).Distinct().Count()}";}catch(System.IO.InvalidDataException ex){return ex.Message;}}}
    public IReadOnlyList<Controls.FilterRow> FilterRows=>Items.Select(p=>new Controls.FilterRow(p.Id,new Dictionary<string,string[]> { ["国家"]=[p.Country.Length==0?"未知":p.Country],["阵营"]=[p.Coalition.Length==0?"未知":p.Coalition],["草稿"]=p.Status=="冲突"?["有草稿","冲突"]:[p.Status.Length==0?"无草稿":"有草稿"] })).ToArray();
    public void Refresh()
    {
        var id=Selected?.Id;Items.Clear();
        foreach(var p in Data.Packs.Values.OrderBy(p=>p.Source.Info.Name))
        {
            var state=StrategicPackEditing.State(p);var op=_store.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.StrategicPack&&o.ObjectName==p.Source.Info.Name);var status="";
            if(op is not null){if(StrategicPackEditing.Resolve(Data,op).Status==DraftResolutionStatus.Active){state=StrategicPackEditing.Read(op);status="有草稿";}else status="冲突";}
            var u=Data.Units.Units.FirstOrDefault(u=>u.Name==state.Unit);Items.Add(new(p,state,u?.Country??"",u?.Coalition??"",status,UnitName(state.Unit),state.Transport.Length==0?Localisation.UiText.T("无运输"):UnitName(state.Transport)));
        }Selected=Items.FirstOrDefault(p=>p.Id==id)??Items.FirstOrDefault();OnPropertyChanged(nameof(FilterRows));View.Refresh();
    }
    public string Suggest(StrategicPackEdit state)
    {
        var names=StrategicPackEditing.ExistingNames(Data.Root);
        names.UnionWith(_store.Operations.Select(o=>o.ObjectName));
        names.UnionWith(_store.Operations.Where(o=>o.TargetKind==DraftTargetKind.StrategicPack).Select(o=>StrategicPackEditing.Read(o).Name));
        return StrategicPackEditing.Name(state.Unit,state.Transport,state.Xp,names);
    }
    public async Task SaveAsync(PackListItem item,StrategicPackEdit state)
    {
        if(!CanEdit)throw new InvalidOperationException("当前不能修改草稿");
        var op=StrategicPackEditing.Operation(item.Source,state);var resolved=StrategicPackEditing.Resolve(Data,op);if(resolved.Status!=DraftResolutionStatus.Active)throw new InvalidOperationException(resolved.Reason);
        if(_store.Operations.Any(o=>o.TargetKind==DraftTargetKind.StrategicPack&&o.Id!=op.Id&&StrategicPackEditing.Read(o).Name==state.Name))throw new InvalidOperationException("同批 Pack 名称重复");
        if(state==StrategicPackEditing.State(item.Source))await _store.RemoveAsync(op.Id);else await _store.UpsertAsync(op);Refresh();_changed();
    }
}
