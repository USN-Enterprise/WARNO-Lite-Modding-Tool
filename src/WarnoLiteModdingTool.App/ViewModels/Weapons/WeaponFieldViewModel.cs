using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Weapons;
using System.IO;

namespace WarnoLiteModdingTool.App.ViewModels.Weapons;

public sealed class WeaponFieldViewModel : ObservableObject
{
    private readonly Func<WeaponFieldViewModel, Task> _persist;
    private CancellationTokenSource? _debounce;
    private string _editValue;
    private string _persistedValue;
    private string _status = string.Empty;
    private bool _locked;
    private Exception? _saveError;
    private string? _savingValue;
    public bool HasUnsavedEdit => !string.Equals(EditValue, _persistedValue, StringComparison.Ordinal) || !_pendingPersistence.IsCompleted;
    private Task _pendingPersistence = Task.CompletedTask;

    public WeaponFieldViewModel(WeaponFieldValue field, DraftOperation? draft, Func<WeaponFieldViewModel, Task> persist)
    {
        Field = field;
        Draft = draft;
        _persist = persist;
        _editValue = draft?.TargetValue ?? field.DisplayValue;
        _persistedValue = _editValue;
    }

    public WeaponFieldValue Field { get; }
    public string EditContext { get; init; } = "";
    public DraftOperation? Draft { get; private set; }
    public string Label => Field.Definition.Label + (Field.Definition.Suffix ?? string.Empty);
    public string Section => Field.Definition.Section;
    public string Group => Field.Definition.Group;
    public string Hint => Field.Definition.Hint;
    public string OriginalParameter => Field.Definition.Owner is WeaponFieldOwner.MountedWeapon or WeaponFieldOwner.Turret || Field.Definition.Key.StartsWith("weapon.salves.", StringComparison.Ordinal) ? Field.Location.FieldPath : Field.Definition.FieldName +
        (Field.Definition.MapKey is null ? "" : "[" + Field.Definition.MapKey + "]") +
        (Field.Definition.ArgumentName is null ? "" : "." + Field.Definition.ArgumentName);
    public string FieldPath => Field.Location.FieldPath;
    public string RawValue => Field.RawValue;
    public string SourceLocation => $"{Field.Location.RelativeSourceFile}:{Field.Location.LineNumber}";
    public IReadOnlyList<string> Choices => Field.Definition.ValueKind == WeaponValueKind.Boolean ? ["是", "否"] : Field.Choices;
    public sealed record ReferenceChoice(string Value, string Label, string Search)
    { public override string ToString()=>Search; }
    private IReadOnlyList<ReferenceChoice>? _referenceChoices;
    public IReadOnlyList<ReferenceChoice> ReferenceChoices => _referenceChoices ??= Choices.Select(v=>new ReferenceChoice(v,v,v)).ToArray();
    public ReferenceChoice? SelectedReference {get=>ReferenceChoices.FirstOrDefault(c=>c.Value==EditValue);set{if(value is not null)EditValue=value.Value;} }
    public void SetAmmoChoices(IReadOnlyList<ReferenceChoice> choices) => _referenceChoices=choices;
    public bool IsReferenceEditor => Field.Definition.ValueKind == WeaponValueKind.Reference;
    public bool IsChoiceEditor => Field.Definition.ValueKind is WeaponValueKind.Boolean or WeaponValueKind.Choice;
    public bool IsTextEditor => !IsChoiceEditor && !IsReferenceEditor;
    public bool HasDraft => Draft is not null;
    public bool IsEditable => !_locked && Advanced.EditorMode.CanEdit(Field.Definition.Key);
    public void RefreshMode() => OnPropertyChanged(nameof(IsEditable));
    public string TargetRaw => Draft?.TargetRaw ?? Field.RawValue;

    public string EditValue
    {
        get => _editValue;
        set
        {
            if (!SetProperty(ref _editValue, value ?? string.Empty) || _locked)
            {
                return;
            }

            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = new CancellationTokenSource();
            var token = _debounce.Token;
            _pendingPersistence = PersistLaterAsync(IsChoiceEditor || IsReferenceEditor ? TimeSpan.Zero : TimeSpan.FromMilliseconds(300), token, _pendingPersistence);
        }
    }

    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }

    public async Task FlushAsync()
    {
        _debounce?.Cancel();
        try
        {
            await _pendingPersistence;
        }
        catch (OperationCanceledException)
        {
        }

        if (string.Equals(EditValue, _persistedValue, StringComparison.Ordinal))
        {
            return;
        }

        _pendingPersistence = PersistLaterAsync(TimeSpan.Zero, CancellationToken.None);
        await _pendingPersistence;
        if (_saveError is not null) throw new InvalidOperationException(StatusText, _saveError);
    }

    public void MarkPersisted(DraftOperation? operation, string normalized, string message)
    {
        _saveError = null;
        Draft = operation;
        if (_savingValue is null || EditValue == _savingValue) _editValue = normalized;
        _persistedValue = normalized;
        OnPropertyChanged(nameof(EditValue));
        OnPropertyChanged(nameof(SelectedReference));
        OnPropertyChanged(nameof(HasDraft));
        OnPropertyChanged(nameof(TargetRaw));
        StatusText = message;
    }

    public void Revert(string message)
    {
        _saveError = new InvalidOperationException(message);
        OnPropertyChanged(nameof(SelectedReference));
        StatusText = message;
    }

    public void SetLocked(bool locked)
    {
        _locked = locked;
        OnPropertyChanged(nameof(IsEditable));
    }

    private async Task PersistLaterAsync(TimeSpan delay, CancellationToken token, Task? previous = null)
    {
        if (previous is not null) await previous;
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            token.ThrowIfCancellationRequested();
            _saveError = null;
            _savingValue = EditValue;
            await _persist(this);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _saveError = exception;
            StatusText = $"草稿保存失败：{exception.Message}";
        }
        finally { _savingValue = null; }
    }
}
