namespace WarnoLiteModdingTool.App.ViewModels.Units;

public sealed class UnitChoiceToggleViewModel : ObservableObject
{
    private bool _isSelected;

    public UnitChoiceToggleViewModel(string display, bool isSelected, bool isLocked)
    {
        Display = display;
        _isSelected = isSelected;
        IsLocked = isLocked;
    }

    public string Kind { get; set; } = "";
    public string Display { get; }

    public bool IsLocked { get; }

    public bool IsEnabled => !IsLocked;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? SelectionChanged;

    public void SetSilently(bool selected) => _isSelected = selected;
}
