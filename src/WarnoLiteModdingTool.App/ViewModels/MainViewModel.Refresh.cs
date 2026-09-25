using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.App.ViewModels.Units;
using WarnoLiteModdingTool.App.ViewModels.Weapons;
using WarnoLiteModdingTool.App.ViewModels.Divisions;
using System.IO;
using WarnoLiteModdingTool.Core.Ndf;

namespace WarnoLiteModdingTool.App.ViewModels;

public sealed partial class MainViewModel
{
    private ProjectWorkspaceSnapshot? _snapshot;
    public Task CacheSaveTask { get; private set; } = Task.CompletedTask;
    public Func<bool>? CanSaveCache { get; set; }
    public IReadOnlyDictionary<string, long> LastRefreshTimings { get; private set; } = new Dictionary<string, long>();
    public int LastRefreshReadFiles { get; private set; }
    public long LastRefreshReadBytes { get; private set; }
    public event Action? WorkspaceRefreshing;
    public event Action? WorkspaceRefreshed;
    private void QueueCacheSave(ProjectWorkspaceSnapshot snapshot)
    {
        if (CanSaveCache?.Invoke() == false) return;
        CacheSaveTask = Task.Run(() =>
        {
            try { if (snapshot.Reads.IsStable()) snapshot.Cache.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { /* A rebuildable cache must not turn a successful load or commit into a failure. */ }
        });
    }
    private async Task RefreshAfterApplyAsync(ApplyPreview preview)
    {
        if (_snapshot is not { } previous || _draftStore is null) throw new InvalidOperationException("项目会话不可用，请重新打开项目。");
        var navigation = WorkspaceNavigation.Capture(this, preview);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        IsScanning = true;
        StatusText = "正在更新本次修改及关联数据…";
        try
        {
            WorkspaceRefreshing?.Invoke();
            var cache = await Task.Run(previous.Cache.Next);
            var next = await Task.Run(() => ProjectWorkspaceSnapshot.LoadRootAsync(preview.ProjectRoot, cache, previous: previous, committed: preview.Files));
            var context = next.Context;
            var drafts = await _draftStore.LoadAsync();
            Diagnostics.Clear(); AddProjectDiagnostics(context);
            ApplyIndexResult(next.Index);
            foreach (var message in next.Units.Diagnostics.Concat(next.Weapons?.Diagnostics ?? []).Concat(next.Divisions?.Diagnostics ?? []).Concat(next.Strategic?.Diagnostics ?? []))
                Diagnostics.Add(new DiagnosticItemViewModel("项目数据", message));
            InstallWorkspaces(next, drafts);
            RefreshMode();
            var notice = navigation.Restore(this);
            _snapshot = next; _currentContext = context;
            var times = new Dictionary<string, long>(next.Timings) { ["total"] = timer.ElapsedMilliseconds };
            LastRefreshTimings = times; LastRefreshReadFiles = next.Reads.ReadFiles; LastRefreshReadBytes = next.Reads.ReadBytes;
            QueueCacheSave(next);
            StatusText = notice ?? "应用完成，关联数据已更新";
        }
        catch (Exception ex)
        {
            ProjectLoadCache.CancelPendingSave();
            UnitWorkspace?.RequireRefresh();
            RecordToolProblem("已应用，但界面刷新失败", ex, preview.ProjectRoot);
            throw;
        }
        finally { IsScanning = false; WorkspaceRefreshed?.Invoke(); }
    }
    private void InstallWorkspaces(ProjectWorkspaceSnapshot snapshot, DraftLoadResult draftLoad)
    {
        var store = _draftStore ?? throw new InvalidOperationException("草稿会话不可用");
        var context = snapshot.Context; var result = snapshot.Index;
        if (snapshot.PictureDiagnostic is { } pictureDiagnostic) Diagnostics.Add(new DiagnosticItemViewModel("单位图片", pictureDiagnostic));
        var unitData = snapshot.Units; var weaponData = snapshot.Weapons; var divisionData = snapshot.Divisions;
        var weaponCapability = result.Modules.FirstOrDefault(m => m.Key == "weapons");
        var ammoCapability = result.Modules.FirstOrDefault(m => m.Key == "ammo");
        DivisionWorkspace?.Dispose();
        WeaponWorkspace = null; AmmoWorkspace = null; DivisionWorkspace = null; StrategicWorkspace = null;
        _creationUnits=unitData;_creationWeapons=weaponData;_creationDivisions=divisionData;
        UnitWorkspace = new UnitWorkspaceViewModel(
            unitData,
            weaponData,
            divisionData,
            store,
            draftLoad,
            SetStatusText,
            () => OpenProjectAsync(context.Layout.RootPath), RefreshAfterApplyAsync) { PictureTextures = snapshot.PictureTextures };
        if (weaponData is not null)
        {
            if (weaponCapability?.CanScan == true && weaponData.Weapons.Count > 0)
            {
                WeaponWorkspace = new WeaponWorkspaceViewModel(weaponData, store, UnitWorkspace, SetStatusText);
            }
            if (ammoCapability?.CanScan == true && weaponData.Ammunition.Count > 0)
            {
                AmmoWorkspace = new AmmoWorkspaceViewModel(weaponData, store, UnitWorkspace, SetStatusText);
            }
        }
        if (divisionData?.HasCompleteFileSet == true && divisionData.Divisions.Count > 0)
        {
            DivisionWorkspace = new DivisionWorkspaceViewModel(divisionData, store, SetStatusText, UnitWorkspace.RefreshExternalDraftState);
        }

        if (result.Modules.Any(m => m.Key is "strategic" or "sp" && m.CanScan))
        {
            var strategic = snapshot.Strategic!;
            UnitWorkspace.StrategicData = strategic;
            StrategicWorkspace = new StrategicWorkspaceViewModel(strategic, store, UnitWorkspace.RefreshExternalDraftState, SetStatusText);
            UnitWorkspace.RefreshExternalDraftState();
            OnPropertyChanged(nameof(StrategicWorkspace));
            OnPropertyChanged(nameof(IsStrategicModule));OnPropertyChanged(nameof(IsPackModule));
            OnPropertyChanged(nameof(IsRulesModule));
            foreach (var diagnostic in strategic.Diagnostics) Diagnostics.Add(new DiagnosticItemViewModel("将军模式", diagnostic));
        }
        if (!Modules.Any(m => m.Key == "drafts")) Modules.Add(new ModuleItemViewModel(new ModuleCapability(
            "drafts",
            "草稿总览",
            ModuleAvailability.Available,
            "查看当前项目全部未应用修改",
            [],
            [])));

        RulesWorkspace = new RulesWorkspaceViewModel(unitData.Rules!, store, UnitWorkspace.RefreshExternalDraftState);
        OnPropertyChanged(nameof(RulesWorkspace)); OnPropertyChanged(nameof(IsRulesModule)); OnPropertyChanged(nameof(IsObjectModule));
        ProjectSummary = $"1.9.15 · Unit {UnitWorkspace.Units.Count:N0} · Weapon {weaponData?.Weapons.Count ?? 0:N0} · Ammo {weaponData?.Ammunition.Count ?? 0:N0} · Division {divisionData?.Divisions.Count ?? 0:N0} · Army General {StrategicWorkspace?.Data.Records.Count ?? 0:N0}";
    }
}
