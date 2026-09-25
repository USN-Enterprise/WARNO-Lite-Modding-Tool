using WarnoLiteModdingTool.App.Localisation;
using WarnoLiteModdingTool.App.Settings;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Localisation;

namespace WarnoLiteModdingTool.App.ViewModels.Divisions;

public sealed class DivisionTextEditorViewModel : ObservableObject, IDisposable
{
    private readonly DivisionWorkspaceData _data;
    private readonly DivisionRecord _division;
    private readonly DraftStore _store;
    private readonly Action _changed;
    private readonly SemaphoreSlim _save = new(1, 1);
    private CancellationTokenSource? _extraction;
    private bool _busy, _locked, _disposed;
    private string _status = "", _language;
    public IReadOnlyList<DivisionTextPartViewModel> Parts { get; }
    public string DivisionName => _division.DisplayName;
    public bool HasPendingInput => Parts.Any(p => p.HasInput);
    public bool Busy { get => _busy; private set { SetProperty(ref _busy, value); OnPropertyChanged(nameof(CanEdit)); } }
    public bool CanEdit => !_locked && !Busy && !_store.IsBlocked;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ReferenceLanguage { get => _language; set { if (SetProperty(ref _language, value)) foreach (var p in Parts) p.RefreshReference(value); } }
    public DivisionTextEditorViewModel(DivisionWorkspaceData data, DivisionRecord division, DraftStore store, Action changed)
    {
        _data = data; _division = division; _store = store; _changed = changed; _language = UiText.Current.English ? "US" : "SC";
        Parts = DivisionText.Fields.Select(f => new DivisionTextPartViewModel(this, DivisionText.Read(data, division, f, _language))).ToArray();
        Restore();
    }
    internal void Changed() { Status = "本页有未加入草稿的输入"; OnPropertyChanged(nameof(HasPendingInput)); }
    internal DraftOperation? Draft(string field) => _store.Operations.SingleOrDefault(o => o.TargetKind == DraftTargetKind.DivisionText && o.ObjectName == _division.Name && o.FieldKey == field);
    public void Restore()
    {
        if (HasPendingInput) return;
        foreach (var p in Parts)
        {
            var draft = Draft(p.Baseline.Field);
            var resolved = draft is null ? null : DivisionText.Resolve(_data, draft);
            p.Restore(draft, resolved?.Status == DraftResolutionStatus.Conflict ? resolved.Reason : "");
            p.RefreshReference(_language);
        }
        Status = Parts.Any(p => Draft(p.Baseline.Field) is not null) ? "草稿已恢复" : "正式内容";
    }
    public async Task ExtractAsync(bool force)
    {
        if (Busy || _disposed) return;
        Busy = true; _extraction = new(); Status = "正在提取原版简介";
        try
        {
            var status = await DivisionTextCache.PrepareAsync(force, _extraction.Token, new Progress<string>(s => { if (!_disposed) Status = s; }));
            if (_disposed) return;
            foreach (var part in Parts)
            {
                part.RefreshReference(_language);
                if (part.Baseline.Text is null && !part.HasInput && Draft(part.Baseline.Field) is null)
                    part.LoadOriginal(DivisionText.Read(_data, _division, part.Baseline.Field, _language));
            }
            var resolved = _data.Divisions.Sum(d => DivisionText.Fields.Count(f => DivisionText.Read(_data, d, f, _language).Text is not null));
            Status = status + "\n" + string.Format(UiText.T("已解析师正文：{0}/{1}"), resolved, _data.Divisions.Count * 2);
        }
        catch (OperationCanceledException) { Status = "提取已取消，已有正文和缓存保留"; }
        catch (Exception ex) { Status = "提取失败：" + ex.Message; }
        finally { _extraction?.Dispose(); _extraction = null; Busy = false; }
    }
    public void CancelExtraction() => _extraction?.Cancel();
    public async Task SaveAsync()
    {
        await _save.WaitAsync();
        try
        {
            if (!CanEdit) throw new InvalidOperationException("当前无法保存师正文");
            Busy = true;
            var upserts = new List<DraftOperation>(); var removals = new List<string>();
            foreach (var part in Parts.Where(p => p.HasInput))
            {
                if (part.Error.Length > 0) throw new InvalidOperationException(part.Error);
                var existing = Draft(part.Baseline.Field);
                if (part.Text == part.Baseline.Text) { if (existing is not null) removals.Add(existing.Id); continue; }
                upserts.Add(DivisionText.Operation(_data, _division, part.Baseline, part.Text, _store.Operations.Concat(upserts).ToArray(), existing));
            }
            await _store.ApplyBatchAsync(upserts, removals);
            foreach (var p in Parts) p.MarkSaved();
            Status = "师正文草稿已保存"; _changed(); OnPropertyChanged(nameof(HasPendingInput));
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { Busy = false; _save.Release(); }
    }
    public void RevertInput()
    {
        foreach (var p in Parts) p.MarkSaved(); // allow restoring the last persisted draft, not discarding it
        Restore(); OnPropertyChanged(nameof(HasPendingInput));
    }
    public void SetLocked(bool locked) { _locked = locked; OnPropertyChanged(nameof(CanEdit)); }
    public void Dispose() { _disposed = true; CancelExtraction(); }
}

public sealed class DivisionTextPartViewModel : ObservableObject
{
    private readonly DivisionTextEditorViewModel _owner;
    private string _text = "", _saved = "", _reference = "", _error = "";
    public DivisionTextPart Baseline { get; private set; }
    public string Field => Baseline.Field;
    public string Label => DivisionText.Label(Field);
    public string Source => Baseline.Source;
    public string Token => Baseline.Token;
    public string Error { get => _error; private set { SetProperty(ref _error, value); OnPropertyChanged(nameof(CanEdit)); } }
    public bool CanEdit => Error.Length == 0;
    public bool HasInput => _text != _saved;
    public string Text { get => _text; set { if (SetProperty(ref _text, value)) _owner.Changed(); } }
    public string Reference { get => _reference; private set => SetProperty(ref _reference, value); }
    public DivisionTextPartViewModel(DivisionTextEditorViewModel owner, DivisionTextPart baseline) { _owner = owner; Baseline = baseline; }
    public void RefreshReference(string language) => Reference = VanillaNames.Lookup("UNITS", Baseline.Token, language) ?? "";
    public void AdoptReference() { if (Reference.Length > 0 && CanEdit && _owner.CanEdit) Text = Reference; }
    public void MarkSaved() => _saved = _text;
    public void LoadOriginal(DivisionTextPart part) { Baseline = part; Restore(null, ""); OnPropertyChanged(nameof(Source)); }
    public void Restore(DraftOperation? draft, string conflict)
    {
        Error = Baseline.Error + conflict;
        try { _text = draft is null ? Baseline.Text ?? "" : DivisionText.Payload(draft).Text; }
        catch (Exception ex) { Error = ex.Message; _text = Baseline.Text ?? ""; }
        _saved = _text; OnPropertyChanged(nameof(Text));
    }
}
