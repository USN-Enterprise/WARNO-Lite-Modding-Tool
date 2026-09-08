using System.Collections.ObjectModel;

namespace WarnoLiteModdingTool.App.ViewModels.Units;

public sealed class UnitFilterDimensionViewModel
{
    public UnitFilterDimensionViewModel(
        string key,
        string label,
        IEnumerable<string> values,
        Action<UnitFilterOptionViewModel> changed)
    {
        Key = key;
        Label = label;
        Options = new ObservableCollection<UnitFilterOptionViewModel>(
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .Select(value => new UnitFilterOptionViewModel(key, label, value, changed)));
    }

    public string Key { get; }

    public string Label { get; }

    public ObservableCollection<UnitFilterOptionViewModel> Options { get; }

    public bool HasSelection => Options.Any(option => option.IsSelected);

    public bool Matches(string value) =>
        !HasSelection || Options.Any(option => option.IsSelected && string.Equals(option.Value, value, StringComparison.CurrentCultureIgnoreCase));

    public bool MatchesAny(IEnumerable<string> values)
    {
        if (!HasSelection)
        {
            return true;
        }

        var candidates = values.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        return Options.Any(option => option.IsSelected && candidates.Contains(option.Value));
    }
}

public sealed class UnitFilterOptionViewModel : ObservableObject
{
    private readonly Action<UnitFilterOptionViewModel> _changed;
    private bool _isSelected;

    public UnitFilterOptionViewModel(
        string dimensionKey,
        string dimensionLabel,
        string value,
        Action<UnitFilterOptionViewModel> changed)
    {
        DimensionKey = dimensionKey;
        DimensionLabel = dimensionLabel;
        Value = value;
        _changed = changed;
    }

    public string DimensionKey { get; }

    public string DimensionLabel { get; }

    public string Value { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetSelected(value, true);
    }

    public void SetSelected(bool value, bool notify)
    {
        if (!SetProperty(ref _isSelected, value, nameof(IsSelected)))
        {
            return;
        }

        if (notify)
        {
            _changed(this);
        }
    }
}

public sealed record UnitFilterTagViewModel(string DimensionKey, string DimensionLabel, string Value)
{
    public string DisplayText => $"{DimensionLabel} · {Value}";
}
