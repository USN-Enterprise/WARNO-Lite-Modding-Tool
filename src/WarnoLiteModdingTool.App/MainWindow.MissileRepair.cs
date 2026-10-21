using System.Windows;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.Localisation;

namespace WarnoLiteModdingTool.App;

public partial class MainWindow
{
    private async void MissileRepair_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsModToolsOpen = false;
        try
        {
            await _viewModel.SaveBeforeLeavingAsync();
            if (_viewModel.HasOpenProject && _viewModel.UnitWorkspace?.IsTransactionBusy != true)
                new MissileRepairWindow(_viewModel) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, UiText.T("修复导弹副本")); }
    }
}
