using System;
using System.Windows;
using System.Windows.Controls;

namespace WarnoLiteModdingTool.App.Workspaces;

public partial class DraftWorkspaceView : UserControl
{
    public DraftWorkspaceView() => InitializeComponent();
    private MainWindow Host => (MainWindow)Window.GetWindow(this);
    private void ClearDraftSelection_Click(object sender, RoutedEventArgs e) => Host.ClearDraftSelection_Click(sender, e);
    private void ClearDrafts_Click(object sender, RoutedEventArgs e) => Host.ClearDrafts_Click(sender, e);
    private void DeleteSelectedDrafts_Click(object sender, RoutedEventArgs e) => Host.DeleteSelectedDrafts_Click(sender, e);
    private void DraftGroupSelect_Click(object sender, RoutedEventArgs e) => Host.DraftGroupSelect_Click(sender, e);
    private void DraftGroup_Loaded(object sender, RoutedEventArgs e) => Host.DraftGroup_Loaded(sender, e);
    private void DraftSelection_Click(object sender, RoutedEventArgs e) => Host.DraftSelection_Click(sender, e);
    private void PreviewApply_Click(object sender, RoutedEventArgs e) => Host.PreviewApply_Click(sender, e);
    private void RestoreBackup_Click(object sender, RoutedEventArgs e) => Host.RestoreBackup_Click(sender, e);
    private void SelectAllDrafts_Click(object sender, RoutedEventArgs e) => Host.SelectAllDrafts_Click(sender, e);
}
