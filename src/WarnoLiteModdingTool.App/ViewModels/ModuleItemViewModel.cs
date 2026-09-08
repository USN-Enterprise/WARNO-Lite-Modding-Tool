using WarnoLiteModdingTool.Core.Projects;

namespace WarnoLiteModdingTool.App.ViewModels;

public sealed class ModuleItemViewModel : ObservableObject
{
    private ModuleCapability _capability;

    public ModuleItemViewModel(ModuleCapability capability)
    {
        _capability = capability;
    }

    public string Key => _capability.Key;

    public string DisplayName => _capability.DisplayName;

    public string Summary => _capability.Summary;

    public string StatusText => _capability.Availability switch
    {
        ModuleAvailability.Available => "可用",
        ModuleAvailability.Limited => "部分可用",
        ModuleAvailability.ParseError => "解析异常",
        _ => "不可用"
    };

    public double Opacity => _capability.Availability == ModuleAvailability.Unavailable ? 0.52 : 1;

    public bool CanBrowse => _capability.Availability != ModuleAvailability.Unavailable;

    public void Update(ModuleCapability capability)
    {
        _capability = capability;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Opacity));
        OnPropertyChanged(nameof(CanBrowse));
    }
}

