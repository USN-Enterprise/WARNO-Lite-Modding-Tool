using System.Globalization;
using System.Windows.Threading;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Rules;

namespace WarnoLiteModdingTool.App.ViewModels;

public sealed record TerrainViewModel(TerrainRecord Terrain, IReadOnlyList<TerrainFieldViewModel> Fields);
public sealed class TerrainFieldViewModel : ObservableObject
{
    private readonly TerrainWorkspace _workspace;
    private readonly DraftStore _store;
    private readonly Action _changed;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _dirty, _conflict;
    private int _revision;
    private string _value = "", _reduction = "", _inputError = "", _status = "";
    public TerrainRecord Terrain { get; }
    public TerrainCell Cell { get; }
    public bool CanEdit => !_store.IsBlocked && !_conflict && Terrain.Error.Length == 0 && Cell.Error.Length == 0;
    public bool CanEditBasic => CanEdit && TerrainWorkspace.Number(_value, out var value) && value is >= 0 and <= 1;
    public bool HasPendingError => _dirty;
    public string Id => DraftOperation.CreateId(DraftTargetKind.TerrainField, Terrain.File, Terrain.Name, Cell.Key);
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Value
    {
        get => _value;
        set { if (SetProperty(ref _value, value)) { _inputError = ""; SetReduction(); Dirty(); } }
    }
    public string Reduction
    {
        get => _reduction;
        set
        {
            if (!SetProperty(ref _reduction, value)) return;
            if (!TerrainWorkspace.Number(value, out var p) || p is < 0 or > 100) _inputError = "减伤比例必须在0%至100%之间";
            else { _inputError = ""; _value = (1 - p / 100).ToString(CultureInfo.InvariantCulture); OnPropertyChanged(nameof(Value)); }
            Dirty();
        }
    }
    public TerrainFieldViewModel(TerrainWorkspace workspace, TerrainRecord terrain, TerrainCell cell, DraftStore store, Action changed)
    {
        _workspace = workspace; Terrain = terrain; Cell = cell; _store = store; _changed = changed;
        Restore(); _timer.Tick += async (_, _) => { _timer.Stop(); await SaveAsync(); };
    }
    private void SetReduction()
    {
        _reduction = TerrainWorkspace.Number(_value, out var value) && value is >= 0 and <= 1 ? ((1 - value) * 100).ToString(CultureInfo.InvariantCulture) : "";
        OnPropertyChanged(nameof(Reduction)); OnPropertyChanged(nameof(CanEditBasic));
    }
    private void Dirty() { _dirty = true; _revision++; Status = "尚未保存"; _timer.Stop(); _timer.Start(); }
    public void Restore()
    {
        if (_dirty) return;
        _timer.Stop();
        var draft = _store.Operations.SingleOrDefault(o => o.Id == Id);
        var resolved = draft is null ? null : _workspace.Resolve(draft);
        _conflict = resolved?.Status == DraftResolutionStatus.Conflict;
        _value = draft?.TargetRaw ?? Cell.Raw;
        if (Cell.Boolean && bool.TryParse(_value, out var flag)) _value = flag ? "true" : "false";
        SetReduction(); OnPropertyChanged(nameof(Value)); OnPropertyChanged(nameof(CanEdit));
        Status = _conflict ? resolved!.Reason : Terrain.Error + Cell.Error + (draft is null ? "" : "草稿已恢复");
    }
    public async Task SaveAsync()
    {
        _timer.Stop(); await _gate.WaitAsync();
        try
        {
            while (_dirty)
            {
                if (!CanEdit) throw new InvalidOperationException("地形参数无法编辑，请先处理冲突");
                if (_inputError.Length > 0) throw new InvalidOperationException(_inputError);
                var revision = _revision;
                var op = _workspace.Operation(Terrain, Cell, Value.Trim());
                var same = Cell.Boolean ? bool.Parse(op.TargetRaw) == bool.Parse(Cell.Raw) : decimal.Parse(op.TargetRaw, NumberStyles.Float, CultureInfo.InvariantCulture) == decimal.Parse(Cell.Raw, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (same) await _store.RemoveAsync(Id); else await _store.UpsertAsync(op);
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
        try { await _store.RemoveAsync(Id); _dirty = false; _inputError = ""; Restore(); _changed(); }
        catch (Exception ex) { Status = ex.Message; }
        finally { _gate.Release(); }
    }
}
