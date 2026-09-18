using System.Windows.Threading;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Rules;

namespace WarnoLiteModdingTool.App.ViewModels;

public sealed record ExperienceRouteViewModel(ExperienceRoute Route, IReadOnlyList<ExperienceLevelViewModel> Levels);

public sealed class ExperienceLevelViewModel : ObservableObject
{
    private readonly ExperienceWorkspace _workspace;
    private readonly DraftStore _store;
    private readonly Action _changed;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _revision;
    private bool _dirty, _restoring, _conflict;
    private string _status = "";
    public ExperienceRoute Route { get; }
    public ExperienceLevel Level { get; }
    public IReadOnlyList<ExperienceCellViewModel> Cells { get; }
    public bool CanEdit => !_store.IsBlocked && !_conflict && Route.Error.Length == 0 && Level.Error.Length == 0;
    public bool HasPendingError => _dirty;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Id => DraftOperation.CreateId(DraftTargetKind.ExperienceLevel, Route.File, Route.Name, Level.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public ExperienceLevelViewModel(ExperienceWorkspace workspace, ExperienceRoute route, ExperienceLevel level, DraftStore store, Action changed)
    {
        _workspace = workspace; Route = route; Level = level; _store = store; _changed = changed;
        Cells = level.Cells.Select(c => new ExperienceCellViewModel(c, MarkDirty)).ToArray();
        Restore();
        _timer.Tick += async (_, _) => { _timer.Stop(); await SaveAsync(); };
    }
    private void MarkDirty()
    {
        if (_restoring) return;
        _revision++; _dirty = true; Status = "尚未保存"; _timer.Stop(); _timer.Start();
    }
    public void Restore()
    {
        _timer.Stop(); _restoring = true;
        try
        {
            var draft = _store.Operations.FirstOrDefault(o => o.Id == Id);
            var resolved = draft is null ? null : _workspace.Resolve(draft);
            _conflict = resolved?.Status == DraftResolutionStatus.Conflict;
            var values = draft is not null && !_conflict ? ExperienceWorkspace.Values(draft) : null;
            foreach (var cell in Cells) cell.Value = values?.GetValueOrDefault(cell.Cell.Key) ?? cell.Cell.Raw;
            Status = _conflict ? resolved!.Reason : Route.Error.Length > 0 ? Route.Error : Level.Error.Length > 0 ? Level.Error : draft is null ? "" : "草稿已恢复";
            _revision++; _dirty = false; OnPropertyChanged(nameof(CanEdit));
        }
        finally { _restoring = false; }
    }
    public async Task SaveAsync()
    {
        _timer.Stop(); await _gate.WaitAsync();
        try
        {
            while (_dirty)
            {
                if (!CanEdit) throw new InvalidOperationException("当前等级无法编辑，请先处理诊断或撤销冲突草稿");
                var revision = _revision;
                var values = Cells.Where(c => c.Cell.Error.Length == 0).ToDictionary(c => c.Cell.Key, c => c.Value.Trim());
                var op = _workspace.Operation(Route, Level, values);
                if (Cells.Where(c => c.Cell.Error.Length == 0).All(c => c.Cell.Raw == values[c.Cell.Key])) await _store.RemoveAsync(op.Id);
                else await _store.UpsertAsync(op);
                if (revision == _revision) _dirty = false;
                Status = "草稿已保存"; _changed();
            }
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { _gate.Release(); }
    }
    public async Task UndoAsync()
    {
        _timer.Stop(); await _gate.WaitAsync();
        try { await _store.RemoveAsync(Id); Restore(); Status = "已撤销草稿"; _changed(); }
        catch (Exception ex) { Status = ex.Message; }
        finally { _gate.Release(); }
    }
}

public sealed class ExperienceCellViewModel(ExperienceCell cell, Action changed) : ObservableObject
{
    private string _value = cell.Raw;
    public ExperienceCell Cell { get; } = cell;
    public string Value { get => _value; set { if (SetProperty(ref _value, value)) changed(); } }
}
