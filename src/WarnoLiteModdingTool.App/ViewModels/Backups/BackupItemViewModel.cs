using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.App.ViewModels.Backups;

public sealed class BackupItemViewModel(BackupSummary backup) : ObservableObject
{
    private bool _workspaceAllowsRestore = true;
    public BackupSummary Backup { get; } = backup;

    public string BackupId => Backup.BackupId;

    public string Header => $"{KindText} · {Backup.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";

    public string KindText => Backup.Kind switch
    {
        "Apply" => "应用备份",
        "Restore" => "恢复前备份",
        _ => Backup.Kind
    };

    public string StateText => Backup.State switch
    {
        "Completed" => "已完成",
        "CompletedWithWarning" => "已完成（有警告）",
        "Prepared" => "已准备，尚未提交",
        "Committing" => "提交中断，可恢复",
        "Committed" => "已提交，收尾未完成",
        "RecoveryRequired" => "需要恢复",
        "RolledBack" => "已回滚",
        "Invalid" => "清单无效",
        _ => Backup.State
    };

    public string FilesText => Backup.Files.Count == 0 ? "—" : string.Join("；", Backup.Files);

    public string ErrorText => Backup.Error ?? string.Empty;

    public bool HasError => ErrorText.Length > 0;

    public bool CanRestore => Backup.CanRestore && _workspaceAllowsRestore;

    public void SetWorkspaceAllowsRestore(bool value)
    {
        if (_workspaceAllowsRestore == value)
        {
            return;
        }

        _workspaceAllowsRestore = value;
        OnPropertyChanged(nameof(CanRestore));
    }
}
