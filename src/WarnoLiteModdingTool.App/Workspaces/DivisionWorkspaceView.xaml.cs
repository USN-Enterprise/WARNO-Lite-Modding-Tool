using System;
using System.Windows;
using System.Windows.Controls;

namespace WarnoLiteModdingTool.App.Workspaces;

public partial class DivisionWorkspaceView : UserControl
{
    public DivisionWorkspaceView() => InitializeComponent();
    private async void CreateDivision_Click(object sender,RoutedEventArgs e)=>await Identity(true);
    private async void EditDivisionIdentity_Click(object sender,RoutedEventArgs e)=>await Identity(false);
    private async System.Threading.Tasks.Task Identity(bool create){try{if(DataContext is WarnoLiteModdingTool.App.ViewModels.MainViewModel vm)await vm.EditDivisionIdentityAsync(Window.GetWindow(this),create);}catch(Exception ex){WarnoLiteModdingTool.App.Localisation.LocalizedMessageBox.Show(Window.GetWindow(this),ex.Message,"无法保存草稿");}}
    private MainWindow Host => (MainWindow)Window.GetWindow(this);
    private void AddDivisionRule_Click(object sender, RoutedEventArgs e) => Host.AddDivisionRule_Click(sender, e);
    private void ApplyDivisionTransportSelection_Click(object sender, RoutedEventArgs e) => Host.ApplyDivisionTransportSelection_Click(sender, e);
    private void CancelDivisionTransportSelection_Click(object sender, RoutedEventArgs e) => Host.CancelDivisionTransportSelection_Click(sender, e);
    private void ClearDrafts_Click(object sender, RoutedEventArgs e) => Host.ClearDrafts_Click(sender, e);
    private void DivisionTag_Click(object sender,RoutedEventArgs e) => Host.DivisionTag_Click(sender, e);
    private void PreviewApply_Click(object sender, RoutedEventArgs e) => Host.PreviewApply_Click(sender, e);
    private void RemoveDivisionRule_Click(object sender, RoutedEventArgs e) => Host.RemoveDivisionRule_Click(sender, e);
    private void RemoveDivisionTransport_Click(object sender, RoutedEventArgs e) => Host.RemoveDivisionTransport_Click(sender, e);
    private void RemoveStandout_Click(object sender,RoutedEventArgs e) => Host.RemoveStandout_Click(sender, e);
    private void SelectDivisionType_Click(object sender, RoutedEventArgs e) => Host.SelectDivisionType_Click(sender, e);
    private void SelectTransports_Click(object sender, RoutedEventArgs e) => Host.SelectTransports_Click(sender, e);
    private void StandoutUnits_Click(object sender,RoutedEventArgs e) => Host.StandoutUnits_Click(sender, e);
    private void TransportFilter_Changed(object? sender, EventArgs e) => Host.TransportFilter_Changed(sender, e);
    private void WorkspaceFilter_Changed(object? sender, EventArgs e) => Host.WorkspaceFilter_Changed(sender, e);
}
