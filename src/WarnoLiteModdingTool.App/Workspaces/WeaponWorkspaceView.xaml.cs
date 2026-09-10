using System;
using System.Windows;
using System.Windows.Controls;

namespace WarnoLiteModdingTool.App.Workspaces;

public partial class WeaponWorkspaceView : UserControl
{
    public WeaponWorkspaceView() => InitializeComponent();
    private MainWindow Host => (MainWindow)Window.GetWindow(this);
    private void ClearDrafts_Click(object sender, RoutedEventArgs e) => Host.ClearDrafts_Click(sender, e);
    private void ClearWeaponScopeSelection_Click(object sender, RoutedEventArgs e) => Host.ClearWeaponScopeSelection_Click(sender, e);
    private void PreviewApply_Click(object sender, RoutedEventArgs e) => Host.PreviewApply_Click(sender, e);
    private void SelectVisibleWeaponScope_Click(object sender, RoutedEventArgs e) => Host.SelectVisibleWeaponScope_Click(sender, e);
    private void UndoWeaponField_Click(object sender, RoutedEventArgs e) => Host.UndoWeaponField_Click(sender, e);
    private void WeaponScopeCheck_Click(object sender, RoutedEventArgs e) => Host.WeaponScopeCheck_Click(sender, e);
    private void WorkspaceFilter_Changed(object? sender, EventArgs e) => Host.WorkspaceFilter_Changed(sender, e);
}
