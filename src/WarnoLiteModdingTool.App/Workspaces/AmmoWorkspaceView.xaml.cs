using System;
using System.Windows;
using System.Windows.Controls;

namespace WarnoLiteModdingTool.App.Workspaces;

public partial class AmmoWorkspaceView : UserControl
{
    public AmmoWorkspaceView() => InitializeComponent();
    private MainWindow Host => (MainWindow)Window.GetWindow(this);
    private void ClearDrafts_Click(object sender, RoutedEventArgs e) => Host.ClearDrafts_Click(sender, e);
    private void PreviewApply_Click(object sender, RoutedEventArgs e) => Host.PreviewApply_Click(sender, e);
    private void UndoAmmoField_Click(object sender, RoutedEventArgs e) => Host.UndoAmmoField_Click(sender, e);
    private void WorkspaceFilter_Changed(object? sender, EventArgs e) => Host.WorkspaceFilter_Changed(sender, e);
}
