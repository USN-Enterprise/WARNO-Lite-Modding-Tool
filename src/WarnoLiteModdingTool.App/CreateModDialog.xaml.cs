using System.Windows;
using System.IO;
using Microsoft.Win32;
using WarnoLiteModdingTool.App.ViewModels;

namespace WarnoLiteModdingTool.App;

public partial class CreateModDialog : Window
{
    private readonly MainViewModel _viewModel;
    private bool _ready;

    public CreateModDialog(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        ModsRootTextBox.Text = viewModel.SuggestedModsRoot() ?? string.Empty;
        _ready = true;
        ValidateInputs();
        ModNameTextBox.Focus();
    }

    public string ModsRoot => ModsRootTextBox.Text.Trim();

    public string ModName => ModNameTextBox.Text.Trim();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含 CreateNewMod.bat 的 WARNO Mods 文件夹",
            Multiselect = false,
            InitialDirectory = Directory.Exists(ModsRoot) ? ModsRoot : null
        };
        if (dialog.ShowDialog(this) == true)
        {
            ModsRootTextBox.Text = dialog.FolderName;
        }
    }

    private void Input_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_ready)
        {
            ValidateInputs();
        }
    }

    private void ValidateInputs()
    {
        CreateButton.IsEnabled = false;
        if (string.IsNullOrWhiteSpace(ModsRoot))
        {
            ValidationText.Text = "请选择 WARNO Mods 文件夹。";
            return;
        }

        try
        {
            var capability = _viewModel.ProbeModCreation(ModsRoot);
            if (!capability.IsAvailable)
            {
                ValidationText.Text = capability.AvailabilityReason;
                return;
            }

            var error = _viewModel.ValidateModName(ModsRoot, ModName);
            if (error is not null)
            {
                ValidationText.Text = error;
                return;
            }

            ValidationText.Text = $"将创建：{Path.Combine(capability.ModsRoot, ModName)}";
            CreateButton.IsEnabled = true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            ValidationText.Text = exception.Message;
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        ValidateInputs();
        if (CreateButton.IsEnabled)
        {
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
