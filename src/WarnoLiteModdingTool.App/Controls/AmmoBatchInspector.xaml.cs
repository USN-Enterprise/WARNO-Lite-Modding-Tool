using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.ViewModels.Weapons;

namespace WarnoLiteModdingTool.App.Controls;

public partial class AmmoBatchInspector : UserControl
{
    public AmmoBatchInspector() => InitializeComponent();
    private async void Run(bool save,AmmoBatchFieldViewModel? field = null)
    {
        if(DataContext is not AmmoWorkspaceViewModel vm) return;
        try { await vm.RunBatchAsync(save,field); }
        catch(Exception ex) { MessageBox.Show(Window.GetWindow(this),ex.Message,Localisation.UiText.T("批量修改弹药")); }
    }
    private void Preview_Click(object sender,RoutedEventArgs e) => Run(false);
    private void Save_Click(object sender,RoutedEventArgs e) => Run(true);
    private void CommonSave_Click(object sender,RoutedEventArgs e) { if((sender as FrameworkElement)?.DataContext is AmmoBatchFieldViewModel f) Run(true,f); }
    private void Apply_Click(object sender,RoutedEventArgs e) => FloatingPanels.Host(this).PreviewApply_Click(sender,e);
}
