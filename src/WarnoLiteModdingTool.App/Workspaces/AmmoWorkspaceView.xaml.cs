using System;
using System.Windows;
using System.Windows.Controls;

namespace WarnoLiteModdingTool.App.Workspaces;

public partial class AmmoWorkspaceView : UserControl
{
    public AmmoWorkspaceView() => InitializeComponent();
    private ViewModels.Weapons.AmmoWorkspaceViewModel? Workspace => (DataContext as ViewModels.MainViewModel)?.AmmoWorkspace;
    private void SelectAmmoBatch_Click(object sender, RoutedEventArgs e) => Workspace?.SelectFilteredBatch(true);
    private void ClearAmmoBatch_Click(object sender, RoutedEventArgs e) => Workspace?.SelectFilteredBatch(false);
    private async void Damage_Click(object sender,RoutedEventArgs e)
    {
        var button=(Button)sender;button.IsEnabled=false;
        try{if(DataContext is ViewModels.MainViewModel vm && vm.AmmoWorkspace is {} workspace)await workspace.OpenDamageAsync(Window.GetWindow(this));}
        catch(Exception ex){MessageBox.Show(Window.GetWindow(this),Localisation.UiText.T(ex.Message),Localisation.UiText.T("距离规则与伤害查询"));}
        finally{button.IsEnabled=true;}
    }
    private MainWindow Host => WarnoLiteModdingTool.App.Controls.FloatingPanels.Host(this);
    private void ClearDrafts_Click(object sender, RoutedEventArgs e) => Host.ClearDrafts_Click(sender, e);
    private void PreviewApply_Click(object sender, RoutedEventArgs e) => Host.PreviewApply_Click(sender, e);
    private void UndoAmmoField_Click(object sender, RoutedEventArgs e) => Host.UndoAmmoField_Click(sender, e);
    private void WorkspaceFilter_Changed(object? sender, EventArgs e) => Host.WorkspaceFilter_Changed(sender, e);
}
