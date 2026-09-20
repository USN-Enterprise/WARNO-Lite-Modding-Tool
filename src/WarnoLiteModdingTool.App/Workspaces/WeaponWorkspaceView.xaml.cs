using System;
using System.Windows;
using System.Windows.Controls;

namespace WarnoLiteModdingTool.App.Workspaces;

public partial class WeaponWorkspaceView : UserControl
{
    public WeaponWorkspaceView() => InitializeComponent();
    private async void WeaponBatch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm && vm.WeaponWorkspace is {} workspace)
        {
            try { await workspace.OpenBatchAsync(Window.GetWindow(this)); }
            catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, Localisation.UiText.T("批量修改武器")); }
        }
    }
    private MainWindow Host => WarnoLiteModdingTool.App.Controls.FloatingPanels.Host(this);
    private void ClearDrafts_Click(object sender, RoutedEventArgs e) => Host.ClearDrafts_Click(sender, e);
    private void ClearWeaponScopeSelection_Click(object sender, RoutedEventArgs e) => Host.ClearWeaponScopeSelection_Click(sender, e);
    private void PreviewApply_Click(object sender, RoutedEventArgs e) => Host.PreviewApply_Click(sender, e);
    private void SelectVisibleWeaponScope_Click(object sender, RoutedEventArgs e) => Host.SelectVisibleWeaponScope_Click(sender, e);
    private void UndoWeaponField_Click(object sender, RoutedEventArgs e) => Host.UndoWeaponField_Click(sender, e);
    private void WeaponScopeCheck_Click(object sender, RoutedEventArgs e) => Host.WeaponScopeCheck_Click(sender, e);
    private void WorkspaceFilter_Changed(object? sender, EventArgs e) => Host.WorkspaceFilter_Changed(sender, e);
}
