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
    public string ProjectRoot { get; init; } = "";
    public Core.Localisation.UnitLocalisationCatalog? LocalisationCatalog { get; init; }
    public WeaponFieldViewModel? LinkedTags { get; set; }
    public bool IsGuidanceReview => Field.Definition.FieldName == "IsFireAndForget" && Advanced.EditorMode.IsAdvanced;
    public string EditContext { get; init; } = "";
    public DraftOperation? Draft { get; private set; }
    public string Label => Field.Definition.Label + (Field.Definition.Suffix ?? string.Empty);
    public string Section => Field.Definition.Section;
    public string Group => Field.Definition.Group;
    public string Hint => Field.Definition.FieldName == "IsFireAndForget" ? "开启或关闭后请核对专业模式的弹药说明标签；不自动猜测manual、semiAuto或F&F。" : Field.Definition.Hint;
    public string OriginalParameter => Field.Definition.Owner is WeaponFieldOwner.MountedWeapon or WeaponFieldOwner.Turret || Field.Definition.Key.StartsWith("weapon.salves.", StringComparison.Ordinal) ? Field.Location.FieldPath : Field.Definition.FieldName +
        (Field.Definition.MapKey is null ? "" : "[" + Field.Definition.MapKey + "]") +
        (Field.Definition.ArgumentName is null ? "" : "." + Field.Definition.ArgumentName);
    public string FieldPath => Field.Location.FieldPath;
    public string RawValue => Field.RawValue;
    public string SourceLocation => $"{Field.Location.RelativeSourceFile}:{Field.Location.LineNumber}";
    public IReadOnlyList<string> Choices => Field.Definition.ValueKind == WeaponValueKind.Boolean ? ["是", "否"] : Field.Definition.ValueKind == WeaponValueKind.CatalogChoice ? Field.Choices.Select(Core.Ndf.NdfSyntaxDocument.Unquote).ToArray() : Field.Choices;
    public sealed record ReferenceChoice(string Value, string Label, string Search)
    { public override string ToString()=>Search; }
    private IReadOnlyList<ReferenceChoice>? _referenceChoices;
    public IReadOnlyList<ReferenceChoice> ReferenceChoices => _referenceChoices ??= Choices.Select(v =>
    {
        var label = v;
        if (Field.Definition.FieldName == "WeaponDescriptionToken")
            label = LocalisationCatalog?.TryResolve(v, out var custom) == true ? custom : Core.Localisation.VanillaNames.Lookup("UNITS", v, Localisation.UiText.Current.English ? "US" : "SC") ?? v;
        return new ReferenceChoice(v, label, label == v ? v : v + " · " + label);
    }).ToArray();
    public ReferenceChoice? SelectedReference {get=>ReferenceChoices.FirstOrDefault(c=>c.Value==EditValue);set{if(value is not null)EditValue=value.Value;} }
    public void SetAmmoChoices(IReadOnlyList<ReferenceChoice> choices) => _referenceChoices=choices;
    public bool IsReferenceEditor => Field.Definition.ValueKind == WeaponValueKind.Reference;
    public bool IsChoiceEditor => Field.Definition.ValueKind is WeaponValueKind.Boolean or WeaponValueKind.Choice;
    public bool IsAdvancedEditor => Field.Definition.ValueKind is WeaponValueKind.CatalogChoice or WeaponValueKind.Tags;
    public bool IsTextEditor => !IsChoiceEditor && !IsReferenceEditor && !IsAdvancedEditor;
    public bool HasDraft => Draft is not null;
    public bool BatchLocked { get; init; }
    public bool StructureLocked { get; init; }
    public bool IsEditable => Field.CanEdit && (!Field.IsMissing || Advanced.EditorMode.IsAdvanced) && !BatchLocked && !StructureLocked && !_locked && Advanced.EditorMode.CanEdit(Field.Definition.Key);
    public void RefreshMode() => OnPropertyChanged(nameof(IsEditable));
    public string TargetRaw => Draft?.TargetRaw ?? Field.RawValue;

    public string EditValue
    {
        get => _editValue;
        set
        {
            if (!SetProperty(ref _editValue, value ?? string.Empty) || _locked || BatchLocked || StructureLocked)
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

    public string StatusText { get => StructureLocked ? "存在武器槽草稿，请在武器槽编辑器中继续编辑" : BatchLocked ? "存在相关批量草稿，请在草稿中心应用或移除相关批次" : !Field.CanEdit ? Field.Reason : _status.Length > 0 ? _status : Field.IsMissing ? "未显式设置；选择值后新增" : ""; private set => SetProperty(ref _status, value); }

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
