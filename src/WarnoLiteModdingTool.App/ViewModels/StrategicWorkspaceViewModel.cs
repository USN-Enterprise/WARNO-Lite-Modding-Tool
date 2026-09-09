using System.Collections.ObjectModel;
using System.IO;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Strategic;

namespace WarnoLiteModdingTool.App.ViewModels;

public sealed class StrategicNode : ObservableObject
{
    private string _name = "";
    private bool _hq;
    private string _unit = "";
    private string _transport = "";
    private int _xp;
    private int _count = 1;
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public string Pack { get; set; } = "";
    public int? Start { get; set; }
    public Func<string,string>? TranslateName { get; set; }
    public string EditableName { get => TranslateName?.Invoke(Name) ?? Name; set => Name = value; }
    public Action? Changed { get; set; }
    public ObservableCollection<StrategicNode> Children { get; init; } = [];
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) Notify(); } }
    public bool IsHQ { get => _hq; set { if (SetProperty(ref _hq, value)) Notify(); } }
    public string Unit { get => _unit; set { if (SetProperty(ref _unit, value)) Notify(); } }
    public string Transport { get => _transport; set { if (SetProperty(ref _transport, value)) Notify(); } }
    public int Xp { get => _xp; set { if (SetProperty(ref _xp, value)) Notify(); } }
    public int Count { get => _count; set { if (SetProperty(ref _count, value)) Notify(); } }
    public Func<string, string>? UnitName { get; set; }
    public string Display => Kind == "unit" ? $"{UnitName?.Invoke(Unit) ?? Unit} × {Count} · XP {Xp}" : (TranslateName?.Invoke(Name) ?? Name) + (IsHQ ? " · HQ" : "");
    public void RefreshLabel() { OnPropertyChanged(nameof(Display)); OnPropertyChanged(nameof(EditableName)); }
    private void Notify() { OnPropertyChanged(nameof(Display)); Changed?.Invoke(); }
}

public sealed class StrategicWorkspaceViewModel : ObservableObject
{
    private readonly DraftStore _store;
    private readonly Action _refresh;
    private readonly Action<string> _status;
    private Task _pending = Task.CompletedTask;
    private Exception? _saveError;
    private readonly Dictionary<string, DraftOperation> _unsaved = [];
    private bool _loading;
    private bool _locked;
    private bool _conflict;
    private bool _originalRoster;
    private StrategicRecord? _selected;
    private string _search = "";
    private string _error = "";
    private string _name = "";
    private readonly Dictionary<string, string> _pawn = new();
    public StrategicWorkspaceViewModel(StrategicWorkspace data, DraftStore store, Action refresh, Action<string> status)
    { Data = data; _store = store; _refresh = refresh; _status = status; Selected = data.Records.FirstOrDefault(); }
    public StrategicWorkspace Data { get; }
    public ObservableCollection<StrategicNode> Companies { get; } = [];
    public ObservableCollection<StrategicNode> Roots { get; } = [];
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) OnPropertyChanged(nameof(VisibleRecords)); } }
    public Func<string,bool>? ListFilter {get;set;}
    public void RefreshList(){var changed=_store.Operations.Select(o=>o.ObjectName).ToHashSet();foreach(var row in FilterRows)((Dictionary<string,string[]>)row.Values)["草稿"]=[changed.Contains(row.Id)?"有草稿":"无草稿"];OnPropertyChanged(nameof(VisibleRecords));}
    private IReadOnlyList<Controls.FilterRow>? _filterRows;
    public IReadOnlyList<Controls.FilterRow> FilterRows => _filterRows ??= Data.Records.Select(r=>new Controls.FilterRow(r.Id,new Dictionary<string,string[]> { ["国家"]=[r.Country],["阵营"]=[r.Coalition],["所属师"]=[r.Division],["战斗角色"]=[r.Baseline.PawnValues.GetValueOrDefault("BattleRole")??"未知"],["草稿"]=[_store.Operations.Any(o=>o.ObjectName==r.Id)?"有草稿":"无草稿"] })).ToArray();
    private static string Natural(string text)=>System.Text.RegularExpressions.Regex.Replace(text,"[0-9]+",m=>m.Value.PadLeft(12,'0'));
    public IEnumerable<StrategicRecord> VisibleRecords => Data.Records.Where(r => (ListFilter?.Invoke(r.Id)??true) && ( DisplayName("UNITS",r.Baseline.Name).Contains(Search, StringComparison.OrdinalIgnoreCase) || r.Id.Contains(Search, StringComparison.OrdinalIgnoreCase))).OrderBy(r=>r.Coalition=="PACT"?0:r.Coalition=="NATO"?1:2).ThenBy(r=>r.Country,StringComparer.OrdinalIgnoreCase).ThenBy(r=>Natural(DisplayName("UNITS",r.Baseline.Name)),StringComparer.CurrentCultureIgnoreCase).ThenBy(r=>r.Id,StringComparer.Ordinal);
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public bool CanEdit => !_locked && !_conflict && !_store.IsBlocked && Selected is not null;
    public bool CanEditRoster => CanEdit && Selected?.Error is null;
    public StrategicRecord? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) Restore(); } }
    public string PawnName { get => DisplayName("UNITS", _name); set { if (SetProperty(ref _name, value)) Save(); } }
    public string PawnValue(string key) => _pawn.GetValueOrDefault(key) ?? "";
    public void SetPawnValue(string key, string value) { if (_pawn.GetValueOrDefault(key) == value) return; _pawn[key] = value; Save(); }
    public event Action? SelectionRestored;
    public void SetTransactionLocked(bool value) { _locked = value; OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanEditRoster)); }
    public async Task FlushAsync()
    {
        await _pending;
        foreach (var operation in _unsaved.Values.ToArray())
        {
            _pending = PersistAsync(Task.CompletedTask, operation);
            await _pending;
            if (_unsaved.ContainsKey(operation.Id)) throw new IOException("战略草稿保存失败", _saveError);
        }
    }

    public void Restore()
    {
        _loading = true;
        Roots.Clear();
        Companies.Clear(); _pawn.Clear(); _conflict = false;
        Error = Selected?.Error ?? "";
        if (Selected is { } record)
        {
            var operation = _unsaved.Values.FirstOrDefault(o => o.ObjectName == record.Id) ?? _store.Operations.FirstOrDefault(o => o.TargetKind == DraftTargetKind.StrategicPlan && o.ObjectName == record.Id);
            var state = record.Baseline;
            if (operation is not null)
            {
                var resolved = StrategicPlanner.Resolve(Data, operation);
                if (resolved.Status == DraftResolutionStatus.Active) state = StrategicCodec.Deserialize(operation.TargetValue);
                else { Error = resolved.Reason; _conflict = true; }
            }
            _originalRoster = System.Text.Json.JsonSerializer.Serialize(state.Companies) == System.Text.Json.JsonSerializer.Serialize(record.Baseline.Companies);
            foreach (var company in state.Companies)
            {
                var node = Node(company.Id, "company", company.Name, company.IsHQ);
                foreach (var group in company.Groups)
                {
                    var child = Node(group.Id, "group", group.Name, group.IsHQ);
                    foreach (var slot in group.Slots) child.Children.Add(Node(slot.Id, "unit", "", false, slot));
                    node.Children.Add(child);
                }
                Companies.Add(node);
            }
            _name = state.Name;
            foreach (var pair in state.PawnValues) _pawn[pair.Key] = pair.Value;
        }
        if (Selected is not null) Roots.Add(new StrategicNode { Id=Selected.Id,Kind="root",Name=DisplayName("UNITS",_name),Children=Companies });
        _loading = false;
        OnPropertyChanged(nameof(PawnName)); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanEditRoster));
        SelectionRestored?.Invoke();
    }

    public StrategicNode? Add(StrategicNode? parent)
    {
        if (!CanEditRoster) return null;
        if(parent?.Kind == "root")parent=null;
        var kind = parent is null ? "company" : parent.Kind == "company" ? "group" : "unit";
        if (parent?.Kind == "unit") return null;
        var node = Node("new:" + Guid.NewGuid().ToString("N"), kind, kind == "company" ? "新连" : "新组", false);
        if (kind == "unit") node.Unit = Data.Units.Units.FirstOrDefault()?.Name ?? "";
        (parent?.Children ?? Companies).Add(node); ResetIndices(); Save(); return node;
    }
    public void Delete(StrategicNode node) { if (CanEditRoster) { ParentList(node)?.Remove(node); ResetIndices(); Save(); } }
    public void Move(StrategicNode node, int delta)
    {
        if (!CanEditRoster) return;
        var list = ParentList(node); if (list is null) return;
        var index = list.IndexOf(node); var target = index + delta;
        if (target >= 0 && target < list.Count) { list.Move(index, target); ResetIndices(); Save(); }
    }
    public void MoveTo(StrategicNode node, StrategicNode parent)
    {
        if (!CanEditRoster || !((node.Kind == "group" && parent.Kind == "company") || (node.Kind == "unit" && parent.Kind == "group"))) return;
        var list = ParentList(node); if (list is null || list == parent.Children) return;
        list.Remove(node); parent.Children.Add(node); ResetIndices(); Save();
    }
    public async Task UndoAsync()
    {
        await FlushAsync();
        if (Selected is null) return;
        await _store.RemoveAsync(StrategicCodec.Operation(Selected, Selected.Baseline).Id);
        _locked = false; Restore(); _refresh();
    }
    public IEnumerable<StrategicNode> AllNodes => Companies.SelectMany(c => new[] { c }.Concat(c.Children.SelectMany(g => new[] { g }.Concat(g.Children))));
    private ObservableCollection<StrategicNode>? ParentList(StrategicNode node) => Companies.Contains(node) ? Companies : AllNodes.FirstOrDefault(n => n.Children.Contains(node))?.Children;
    private StrategicNode Node(string id, string kind, string name, bool hq, StrategicSlot? slot = null)
    {
        var node = new StrategicNode { Id = id, Kind = kind, Name = name, IsHQ = hq, Pack = slot?.Pack ?? "", Unit = slot?.Unit ?? "", Transport = slot?.Transport ?? "", Xp = slot?.Xp ?? 0, Count = slot?.Count ?? 1, Start = slot?.Start };
        node.TranslateName = name => { var display = DisplayName(kind == "company" ? "COMPANIES" : "PLATOONS",name); return display == name && name.Length == 10 && !Data.Names.Values.Any(d=>d.ContainsValue(name)) ? (kind == "group" ? Localisation.UiText.T("组") + " " + (int.TryParse(id.Split('/').Last(),out var n)?n+1:1) : id.Replace("Descriptor_CombatGroup_", "").Replace('_', ' ')) : display; };
        node.UnitName = unit => Data.Units.Units.FirstOrDefault(u => u.Name == unit)?.DisplayName ?? unit;
        var count = node.Count; node.Changed = () => { if (!_loading && count != node.Count) { ResetIndices(); count = node.Count; } Save(); }; return node;
    }
    public void RefreshLabels() { foreach(var node in AllNodes)node.RefreshLabel(); if(Roots.Count>0)Roots[0].Name=PawnName; OnPropertyChanged(nameof(PawnName)); OnPropertyChanged(nameof(VisibleRecords)); }
    public string DisplayName(string kind,string name)
    {
        if(Data.Names.GetValueOrDefault(kind)?.ContainsValue(name)==true)return name;
        return WarnoLiteModdingTool.Core.Localisation.VanillaNames.Lookup(kind,name,Localisation.UiText.Current.English?"US":"SC") ?? (kind == "UNITS" && Selected?.Baseline.Name == name ? Selected.DisplayName : name);
    }
    private void ResetIndices() { foreach(var node in AllNodes) node.Start=null; }
    public void ApplyMapping(IReadOnlyList<StrategicMappingRow> rows)
    {
        if(!CanEditRoster || Selected is null)return;
        var occupied=new HashSet<int>();var count=rows.Sum(r=>r.Count);
        foreach(var r in rows) {if(r.Start<0||r.Count<=0||(long)r.Start+r.Count>count)throw new InvalidOperationException("PackIndex 越界或数量无效");for(var i=r.Start;i<r.Start+r.Count;i++)if(!occupied.Add(i))throw new InvalidOperationException("PackIndex 重叠");}
        _loading=true;
        foreach(var r in rows) {r.Node.Start=r.Start;r.Node.Count=r.Count;r.Node.Pack=r.Pack;r.Node.Unit=r.Unit;r.Node.Transport=r.Transport;r.Node.Xp=r.Xp;}
        _loading=false;Save();SelectionRestored?.Invoke();
    }
    public IReadOnlyList<StrategicMappingRow> MappingRows()
    {
        var result=new List<StrategicMappingRow>();var cursor=0;
        foreach(var c in Companies)foreach(var g in c.Children)foreach(var n in g.Children){var start=n.Start??(_originalRoster && int.TryParse(Selected?.Templates.GetValueOrDefault(n.Id+"/start"),out var original)?original:cursor);result.Add(new(n,start,c.Display+" / "+g.Display));cursor=start+n.Count;}return result;
    }
    private void Save()
    {
        if (_loading || !CanEdit || Selected is null) return;
        var state = new StrategicState(Companies.Select(c => new StrategicCompany(c.Id, c.Name, c.IsHQ,
            c.Children.Select(g => new StrategicGroup(g.Id, g.Name, g.IsHQ,
                g.Children.Select(s => new StrategicSlot(s.Id, s.Pack, s.Unit, s.Transport, s.Xp, s.Count, s.Start)).ToArray())).ToArray())).ToArray(),
            new Dictionary<string, string>(_pawn), _name);
        if(Roots.Count>0)Roots[0].Name=PawnName;
        _originalRoster = System.Text.Json.JsonSerializer.Serialize(state.Companies) == System.Text.Json.JsonSerializer.Serialize(Selected.Baseline.Companies);
        var operation = StrategicCodec.Operation(Selected, state);
        _unsaved[operation.Id] = operation;
        var previous = _pending;
        _pending = PersistAsync(previous, operation);
    }
    private async Task PersistAsync(Task previous, DraftOperation operation)
    {
        await previous;
        try
        {
            if (operation.BaselineValue == operation.TargetValue) await _store.RemoveAsync(operation.Id);
            else await _store.UpsertAsync(operation);
            if (_unsaved.TryGetValue(operation.Id, out var saved) && ReferenceEquals(saved, operation)) _unsaved.Remove(operation.Id);
            _saveError = null;
            Error = StrategicPlanner.Resolve(Data, operation).Reason;
            RefreshList(); _refresh(); _status(Error.Length == 0 ? "战略草稿已保存" : Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { _saveError = ex; Error = "战略草稿保存失败：" + ex.Message; _status(Error); }
    }
}

public sealed class StrategicMappingRow(StrategicNode node,int start,string group)
{
    public StrategicNode Node { get; }=node;
    public int Start { get; set; }=start;
    public int Count { get; set; }=node.Count;
    public string Pack { get; set; }=node.Pack;
    public string Unit { get; set; }=node.Unit;
    public string Transport { get; set; }=node.Transport;
    public int Xp { get; set; }=node.Xp;
    public string Group { get; }=group;
}
