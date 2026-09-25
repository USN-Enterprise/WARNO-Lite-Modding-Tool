namespace WarnoLiteModdingTool.App.ViewModels.Units;

public sealed partial class UnitWorkspaceViewModel
{
    public IReadOnlyList<Core.Images.TextureChoice> PictureTextures { get; internal set; } = [];
    internal void RequireRefresh() { RefreshRequired = true; SetTransactionBusy(true); }
    internal void RestoreBatchChecks(IReadOnlySet<string> names)
    {
        _suppressBatchSelectionNotifications = true;
        try { foreach (var unit in Units) unit.IsBatchSelected = names.Contains(unit.InternalName); }
        finally { _suppressBatchSelectionNotifications = false; }
        BatchSelectionChanged();
        RefreshFilter();
    }
}
