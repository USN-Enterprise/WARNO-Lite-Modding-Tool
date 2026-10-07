using System.Windows;
using System.Windows.Controls;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.App.ViewModels.Backups;
using WarnoLiteModdingTool.Core.Changes;
using WarnoLiteModdingTool.Core.SourceWorkspace;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.App.SourceWorkspace;
public partial class SourceWorkspaceWindow
{
    private sealed record HistoryEntry(string Kind,string Id,string State,DateTimeOffset Time,object Record);
    private async void HistoryOverview_Click(object sender,RoutedEventArgs e)
    {
        HistoryEntry? selected=null;
        await Run(async()=>
        {
            await SaveAllAsync();
            var entries=await Task.Run(()=>new TransactionBackupStore(_root).List().Select(b=>new HistoryEntry(T("业务备份","Business backup"),b.BackupId,b.State,b.CreatedUtc,b))
                .Concat(SourceTransactions.List(_root).Select(b=>new HistoryEntry(T("源码事务","Source transaction"),b.Id,b.State,b.Created,b)))
                .Concat(ChangeTransactions.List(_root).Select(b=>new HistoryEntry(T("跨版本还原","Cross-version restore"),b.Id,b.State,b.CreatedUtc,b))).OrderByDescending(b=>b.Time).ToArray());
            var dialog=new Window { Owner=this,Title=T("备份总览","Backup overview"),Width=950,Height=620,WindowStartupLocation=WindowStartupLocation.CenterOwner };
            dialog.SetResourceReference(BackgroundProperty,"BackgroundBrush");dialog.SetResourceReference(ForegroundProperty,"TextBrush");
            var dock=new DockPanel { Margin=new Thickness(14) };dialog.Content=dock;
            var info=new TextBlock { Text=T("按类型进入对应的核对与恢复流程；此列表只读取事务清单。","Open the review and recovery flow for the selected type. This list reads journal metadata only."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10) };DockPanel.SetDock(info,Dock.Top);dock.Children.Add(info);
            var grid=DamageUi.Table((T("类型","Type"),"Kind"),(T("批次","Transaction"),"Id"),(T("状态","State"),"State"),(T("时间","Time"),"Time"));grid.ItemsSource=entries;
            var open=DamageUi.Button(T("打开对应恢复入口","Open recovery"),()=>{ if(grid.SelectedItem is HistoryEntry entry) { selected=entry;dialog.DialogResult=true; } });DockPanel.SetDock(open,Dock.Bottom);dock.Children.Add(open);dock.Children.Add(grid);dialog.ShowDialog();
        });
        if(selected?.Record is SourceJournal) History_Click(sender,e);
        else if(selected?.Record is BackupSummary backup && Owner is MainWindow main) main.RestoreBackup_Click(new Button { DataContext=new BackupItemViewModel(backup) },e);
        else if(selected?.Record is ChangeJournal) new Changes.ChangeRecordsWindow(_main) { Owner=this }.ShowDialog();
    }
}
