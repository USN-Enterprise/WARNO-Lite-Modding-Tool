using System;
using System.Windows;
using System.Windows.Controls;

namespace WarnoLiteModdingTool.App.Workspaces;

public partial class UnitWorkspaceView : UserControl
{
    public UnitWorkspaceView() => InitializeComponent();
    private MainWindow Host => (MainWindow)Window.GetWindow(this);
    private void AddBatchDrafts_Click(object sender, RoutedEventArgs e) => Host.AddBatchDrafts_Click(sender, e);
    private void AddCommonBatchField_Click(object sender, RoutedEventArgs e) => Host.AddCommonBatchField_Click(sender, e);
    private void ClearBatchSelection_Click(object sender, RoutedEventArgs e) => Host.ClearBatchSelection_Click(sender, e);
    private void ClearUnitFilters_Click(object sender, RoutedEventArgs e) => Host.ClearUnitFilters_Click(sender, e);
    private void CloseUnitFilters_Click(object sender, RoutedEventArgs e) => Host.CloseUnitFilters_Click(sender, e);
    private void CreateUnit_Click(object sender,RoutedEventArgs e) => Host.CreateUnit_Click(sender, e);
    private void DeploymentPreset_SelectionChanged(object sender, SelectionChangedEventArgs e) => Host.DeploymentPreset_SelectionChanged(sender, e);
    private void PreviewBatch_Click(object sender, RoutedEventArgs e) => Host.PreviewBatch_Click(sender, e);
    private void ReconPreset_SelectionChanged(object sender, SelectionChangedEventArgs e) => Host.ReconPreset_SelectionChanged(sender, e);
    private void RemoveFilterTag_Click(object sender, RoutedEventArgs e) => Host.RemoveFilterTag_Click(sender, e);
    private void RemoveUnitChoice_Click(object sender, RoutedEventArgs e) => Host.RemoveUnitChoice_Click(sender, e);
    private void SelectFieldChoices_Click(object sender, RoutedEventArgs e) => Host.SelectFieldChoices_Click(sender, e);
    private void SelectFilterDimension_Click(object sender, RoutedEventArgs e) => Host.SelectFilterDimension_Click(sender, e);
    private void SelectVisibleBatch_Click(object sender, RoutedEventArgs e) => Host.SelectVisibleBatch_Click(sender, e);
    private void UndoField_Click(object sender, RoutedEventArgs e) => Host.UndoField_Click(sender, e);
}
