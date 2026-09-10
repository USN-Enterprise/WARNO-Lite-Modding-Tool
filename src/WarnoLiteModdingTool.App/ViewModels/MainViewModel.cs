using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using WarnoLiteModdingTool.App.Diagnostics;
using WarnoLiteModdingTool.App.ViewModels.Units;
using WarnoLiteModdingTool.App.ViewModels.Weapons;
using WarnoLiteModdingTool.App.ViewModels.Divisions;
using WarnoLiteModdingTool.App.ViewModels.Drafts;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ModProjectDetector _detector = new();
    private readonly ProjectIndexer _indexer = new();
    private readonly UnitProjectLoader _unitLoader = new();
    private readonly WeaponProjectLoader _weaponLoader = new();
    private readonly DivisionProjectLoader _divisionLoader = new();
    private readonly RecentProjectStore _recentProjectStore;
    private readonly WarnoModsRootStore _modsRootStore;
    private readonly ModCreationService _modCreationService;
    private readonly ApplicationProblemLog _problemLog;
    private readonly OfficialCommandRunner _officialCommandRunner = new();
    private readonly ModuleItemViewModel _problemModule;
    private CancellationTokenSource? _scanCancellation;
    private DraftStore? _draftStore;
    private ICollectionView _objectsView = new ListCollectionView(new List<NdfObjectInfo>());
    private UnitWorkspaceViewModel? _unitWorkspace;
    private WeaponWorkspaceViewModel? _weaponWorkspace;
    private AmmoWorkspaceViewModel? _ammoWorkspace;
    private DivisionWorkspaceViewModel? _divisionWorkspace;
    public RulesWorkspaceViewModel? RulesWorkspace { get; private set; }
    public bool IsRulesModule => SelectedModule?.Key == "rules" && RulesWorkspace is not null;
    public StrategicWorkspaceViewModel? StrategicWorkspace { get; private set; }
    public bool IsStrategicModule => SelectedModule?.Key == "strategic" && StrategicWorkspace is not null;
    private ModuleItemViewModel? _selectedModule;
    private RecentProjectEntry? _selectedRecentProject;
    private NdfObjectInfo? _selectedObject;
    private DraftItemViewModel? _selectedDraft;
    private ProblemItemViewModel? _selectedProblem;
    private string _projectName = "尚未打开项目";
    private string _projectPath = "请选择一个 WARNO Mod 文件夹";
    private string _projectSummary = "1.3 通过分组编辑、标签筛选和事务校验安全修改 Mod";
    private string _statusText = "等待选择项目";
    private string _searchText = string.Empty;
    private bool _isScanning;
    private bool _advancedMode;
    private double _scanPercent;
    private int _visibleObjectCount;
    private ModProjectContext? _currentContext;
    private string _officialCommandOutput = string.Empty;
    private bool _isOfficialCommandBusy;
    private bool _isModToolsOpen;

    public MainViewModel(
        RecentProjectStore? recentProjectStore = null,
        WarnoModsRootStore? modsRootStore = null,
        ModCreationService? modCreationService = null,
        ApplicationProblemLog? problemLog = null)
    {
        _recentProjectStore = recentProjectStore ?? new RecentProjectStore();
        _modsRootStore = modsRootStore ?? new WarnoModsRootStore();
        _modCreationService = modCreationService ?? new ModCreationService();
        _problemLog = problemLog ?? ApplicationProblemLog.Current;
        _problemModule = new ModuleItemViewModel(new ModuleCapability(
            "problems",
            "问题中心",
            ModuleAvailability.Available,
            "分别查看 Mod 问题与工具问题",
            [],
            []));
        Modules.Add(_problemModule);
        foreach (var report in _problemLog.Reports)
        {
            AddProblem(report);
        }
        _problemLog.Reported += ProblemLog_Reported;
    }

    public ObservableCollection<ModuleItemViewModel> Modules { get; } = [];
    public IReadOnlyList<NdfObjectInfo> ProjectObjects => ObjectsView.SourceCollection.Cast<NdfObjectInfo>().ToArray();
    public void NavigateObject(NdfObjectInfo obj)
    {
        SelectedModule = Modules.FirstOrDefault(m => m.Key == obj.ModuleKey);
        SelectedObject = obj;
        if (obj.ModuleKey == "units" && UnitWorkspace is not null) UnitWorkspace.SelectedUnit = UnitWorkspace.Units.FirstOrDefault(u => u.Unit.Name == obj.Name);
        if (obj.ModuleKey == "weapons" && WeaponWorkspace is not null) WeaponWorkspace.SelectedWeapon = WeaponWorkspace.Weapons.FirstOrDefault(w => w.Name == obj.Name);
        if (obj.ModuleKey == "ammo" && AmmoWorkspace is not null) AmmoWorkspace.SelectedAmmo = AmmoWorkspace.Ammunition.FirstOrDefault(a => a.Name == obj.Name);
        if (obj.ModuleKey == "strategic" && StrategicWorkspace is not null) StrategicWorkspace.Selected = StrategicWorkspace.Data.Records.FirstOrDefault(r => r.Id == obj.Name || r.Deck.Info.Name == obj.Name) ?? StrategicWorkspace.Selected;
    }
    private void RefreshMode()
    {
        Advanced.EditorMode.IsAdvanced = AdvancedMode;
        Localisation.UiText.Current.SetLanguage(Localisation.UiText.Current.Language);
        UnitWorkspace?.RefreshMode();
        RulesWorkspace?.Refresh();
        WeaponWorkspace?.RefreshMode();
        if (AmmoWorkspace is not null) foreach (var field in AmmoWorkspace.Fields) field.RefreshMode();
    }

    public ObservableCollection<RecentProjectEntry> RecentProjects { get; } = [];

    public ObservableCollection<DiagnosticItemViewModel> Diagnostics { get; } = [];

    private WarnoLiteModdingTool.Core.Units.UnitWorkspaceData? _creationUnits;
    private WeaponWorkspaceData? _creationWeapons;
    private DivisionWorkspaceData? _creationDivisions;
    public async Task CreateUnitAsync(Window owner,bool edit=false)
    {
        if(_draftStore is null || _creationUnits is null || _creationWeapons is null)throw new InvalidOperationException("请先打开完整的单位与武器模块");
        var existing=edit?_draftStore.Operations.FirstOrDefault(o=>o.TargetKind==DraftTargetKind.UnitCreate&&o.ObjectName==UnitWorkspace?.SelectedUnit?.InternalName):null;
        if(edit&&existing is null)throw new InvalidOperationException("请选择待创建单位");
        var window=new Controls.UnitCreationWindow(_creationUnits,_creationWeapons,_creationDivisions,_draftStore.Operations,existing){Owner=owner};
        if(window.ShowDialog()!=true||window.Result is not {} operation)return;
        StatusText="正在校验新单位…";
        var root=_creationUnits.Localisation.ProjectRoot;
        await Task.Run(()=>new WarnoLiteModdingTool.Core.Transactions.UnitTransactionService().PrepareApplyAsync(root,[operation]));
        await _draftStore.UpsertAsync(operation);UnitWorkspace?.RefreshExternalDraftState();
        if(UnitWorkspace is {} workspace)workspace.SelectedUnit=workspace.Units.FirstOrDefault(u=>u.InternalName==operation.ObjectName);
        StatusText="新增单位已加入草稿";
    }
    private static string IgnoredPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WarnoLiteModdingTool","ignored-problems.json");
    public ObservableCollection<ProblemItemViewModel> IgnoredProblems {get;}=LoadIgnored();
    private static ObservableCollection<ProblemItemViewModel> LoadIgnored(){try{return new(System.Text.Json.JsonSerializer.Deserialize<ProblemReport[]>(File.ReadAllText(IgnoredPath))!.Select(r=>new ProblemItemViewModel(r)));}catch{return [];}}
    public void IgnoreProblems(IEnumerable<ProblemItemViewModel> items,bool restore=false)
    {
        foreach(var item in items.ToArray()){
            if(restore){IgnoredProblems.Remove(item);AddProblem(item.Report);}
            else {ModProblems.Remove(item);ToolProblems.Remove(item);if(!IgnoredProblems.Any(p=>p.Id==item.Id))IgnoredProblems.Add(item);}
        }
        Directory.CreateDirectory(Path.GetDirectoryName(IgnoredPath)!);File.WriteAllText(IgnoredPath,System.Text.Json.JsonSerializer.Serialize(IgnoredProblems.Select(i=>i.Report)));
        OnPropertyChanged(nameof(ModProblemCount));OnPropertyChanged(nameof(ToolProblemCount));OnPropertyChanged(nameof(ProblemCount));OnPropertyChanged(nameof(ProblemButtonText));
    }
    public ObservableCollection<ProblemItemViewModel> ModProblems { get; } = [];

    public ObservableCollection<ProblemItemViewModel> ToolProblems { get; } = [];

    public ICollectionView ObjectsView
    {
        get => _objectsView;
        private set => SetProperty(ref _objectsView, value);
    }

    public ModuleItemViewModel? SelectedModule
    {
        get => _selectedModule;
        set
        {
            if (SetProperty(ref _selectedModule, value))
            {
                SelectedObject = null;
                RefreshFilter();
                OnPropertyChanged(nameof(IsUnitModule));
                OnPropertyChanged(nameof(IsWeaponModule));
                OnPropertyChanged(nameof(IsAmmoModule));
                OnPropertyChanged(nameof(IsDivisionModule));
                OnPropertyChanged(nameof(IsStrategicModule));
                OnPropertyChanged(nameof(IsRulesModule));
                OnPropertyChanged(nameof(IsDraftModule));
                OnPropertyChanged(nameof(IsProblemModule));
                OnPropertyChanged(nameof(IsObjectModule));
                if (IsWeaponModule)
                {
                    WeaponWorkspace?.RefreshFromDrafts();
                }
                if (IsAmmoModule)
                {
                    AmmoWorkspace?.RefreshFromDrafts();
                }
                if (IsDraftModule)
                {
                    SelectedDraft = UnitWorkspace?.DraftItems.FirstOrDefault();
                }
            }
        }
    }

    public UnitWorkspaceViewModel? UnitWorkspace
    {
        get => _unitWorkspace;
        private set
        {
            if (SetProperty(ref _unitWorkspace, value))
            {
                OnPropertyChanged(nameof(IsUnitModule));
                OnPropertyChanged(nameof(IsDraftModule));
                OnPropertyChanged(nameof(IsObjectModule));
            }
        }
    }

    public bool IsUnitModule => SelectedModule?.Key == "units" && UnitWorkspace is not null;

    public WeaponWorkspaceViewModel? WeaponWorkspace
    {
        get => _weaponWorkspace;
        private set
        {
            if (SetProperty(ref _weaponWorkspace, value))
            {
                OnPropertyChanged(nameof(IsWeaponModule));
                OnPropertyChanged(nameof(IsObjectModule));
            }
        }
    }

    public bool IsWeaponModule => SelectedModule?.Key == "weapons" && WeaponWorkspace is not null;

    public AmmoWorkspaceViewModel? AmmoWorkspace
    {
        get => _ammoWorkspace;
        private set
        {
            if (SetProperty(ref _ammoWorkspace, value))
            {
                OnPropertyChanged(nameof(IsAmmoModule));
                OnPropertyChanged(nameof(IsObjectModule));
            }
        }
    }

    public bool IsAmmoModule => SelectedModule?.Key == "ammo" && AmmoWorkspace is not null;

    public DivisionWorkspaceViewModel? DivisionWorkspace
    {
        get => _divisionWorkspace;
        private set
        {
            if (SetProperty(ref _divisionWorkspace, value))
            {
                OnPropertyChanged(nameof(IsDivisionModule));
                OnPropertyChanged(nameof(IsStrategicModule));
                OnPropertyChanged(nameof(IsRulesModule));
                OnPropertyChanged(nameof(IsObjectModule));
            }
        }
    }

    public bool IsDivisionModule => SelectedModule?.Key == "divisions" && DivisionWorkspace is not null;

    public bool IsDraftModule => SelectedModule?.Key == "drafts" && UnitWorkspace is not null;

    public bool IsProblemModule => SelectedModule?.Key == "problems";

    public bool IsObjectModule => !IsUnitModule && !IsWeaponModule && !IsAmmoModule && !IsDivisionModule && !IsStrategicModule && !IsRulesModule && !IsDraftModule && !IsProblemModule;

    public RecentProjectEntry? SelectedRecentProject
    {
        get => _selectedRecentProject;
        set => SetProperty(ref _selectedRecentProject, value);
    }

    public NdfObjectInfo? SelectedObject
    {
        get => _selectedObject;
        set => SetProperty(ref _selectedObject, value);
    }

    public DraftItemViewModel? SelectedDraft
    {
        get => _selectedDraft;
        set => SetProperty(ref _selectedDraft, value);
    }

    public ProblemItemViewModel? SelectedProblem
    {
        get => _selectedProblem;
        set => SetProperty(ref _selectedProblem, value);
    }

    public string ProjectName
    {
        get => _projectName;
        private set => SetProperty(ref _projectName, value);
    }

    public string ProjectPath
    {
        get => _projectPath;
        private set => SetProperty(ref _projectPath, value);
    }

    public string ProjectSummary
    {
        get => _projectSummary;
        private set => SetProperty(ref _projectSummary, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshFilter();
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(CanOpenProject));
                OnPropertyChanged(nameof(CanRunGenerate));
                OnPropertyChanged(nameof(CanRunDevMode));
                OnPropertyChanged(nameof(CanRunUpload));
                OnPropertyChanged(nameof(CanCreateMod));
            }
        }
    }

    private bool _isSavingBeforeLeave;
    public bool IsSavingBeforeLeave => _isSavingBeforeLeave;
    public bool CanInteract => !_isSavingBeforeLeave;
    public bool CanOpenProject => !IsScanning && !IsOfficialCommandBusy && !_isSavingBeforeLeave;

    public async Task SaveBeforeLeavingAsync()
    {
        if (_isSavingBeforeLeave) throw new InvalidOperationException("正在保存草稿，请稍候。");
        _isSavingBeforeLeave = true;
        OnPropertyChanged(nameof(IsSavingBeforeLeave));
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanOpenProject));
        try
        {
            if (UnitWorkspace is not null) await UnitWorkspace.FlushAsync();
            if (WeaponWorkspace is not null) await WeaponWorkspace.FlushAsync();
            if (AmmoWorkspace is not null) await AmmoWorkspace.FlushAsync();
            if (DivisionWorkspace is not null) await DivisionWorkspace.FlushAsync();
            if (RulesWorkspace is not null) await RulesWorkspace.FlushAsync();
            if (StrategicWorkspace is not null) await StrategicWorkspace.FlushAsync();
        }
        catch (Exception exception)
        {
            StatusText = $"草稿保存失败，已保留当前项目和输入：{exception.Message}";
            RecordToolProblem("草稿保存失败", exception);
            throw;
        }
        finally
        {
            _isSavingBeforeLeave = false;
            OnPropertyChanged(nameof(IsSavingBeforeLeave));
            OnPropertyChanged(nameof(CanInteract));
            OnPropertyChanged(nameof(CanOpenProject));
        }
    }

    public bool AdvancedMode
    {
        get => _advancedMode;
        set { if (SetProperty(ref _advancedMode, value)) RefreshMode(); }
    }

    public double ScanPercent
    {
        get => _scanPercent;
        private set => SetProperty(ref _scanPercent, value);
    }

    public int VisibleObjectCount
    {
        get => _visibleObjectCount;
        private set => SetProperty(ref _visibleObjectCount, value);
    }

    public bool HasOpenProject => _currentContext?.IsRecognized == true;

    public bool CanRunGenerate => CanRunOfficial("generate");

    public bool CanRunDevMode => CanRunOfficial("dev");

    public bool CanRunUpload => CanRunOfficial("upload");

    public bool CanCreateMod => !IsScanning && !IsOfficialCommandBusy;

    public string GenerateAvailability => OfficialAvailability("generate");

    public string DevModeAvailability => OfficialAvailability("dev");

    public string UploadAvailability => OfficialAvailability("upload");

    public bool IsOfficialCommandBusy
    {
        get => _isOfficialCommandBusy;
        private set
        {
            if (SetProperty(ref _isOfficialCommandBusy, value))
            {
                OnPropertyChanged(nameof(CanRunGenerate));
                OnPropertyChanged(nameof(CanRunDevMode));
                OnPropertyChanged(nameof(CanRunUpload));
                OnPropertyChanged(nameof(CanCreateMod));
                OnPropertyChanged(nameof(CanOpenProject));
            }
        }
    }

    public string OfficialCommandOutput
    {
        get => _officialCommandOutput;
        private set
        {
            if (SetProperty(ref _officialCommandOutput, value))
            {
                OnPropertyChanged(nameof(HasOfficialCommandOutput));
            }
        }
    }

    public bool HasOfficialCommandOutput => OfficialCommandOutput.Length > 0;

    public bool IsModToolsOpen
    {
        get => _isModToolsOpen;
        set => SetProperty(ref _isModToolsOpen, value);
    }

    public int ModProblemCount => ModProblems.Count;

    public int ToolProblemCount => ToolProblems.Count;

    public int ProblemCount => ModProblemCount + ToolProblemCount;

    public string ProblemButtonText => $"问题 {ProblemCount}";

    public string ProblemLogRoot => _problemLog.LogRoot;

    public async Task InitializeAsync()
    {
        try
        {
            var recent = await _recentProjectStore.LoadAsync();
            ReplaceRecentProjects(recent);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = "最近项目记录不可用，但不影响打开 Mod";
            RecordToolProblem("最近项目记录不可用", exception);
        }
    }

    public Func<Task>? PrepareNamesAsync { get; set; }
    // Injectable so desktop fixtures never read or write personal caches/settings.
    public Func<string, ProjectLoadCache?>? OpenLoadCache { get; set; }
    public bool LastOpenUsedCache { get; private set; }
    public long LastOpenMilliseconds { get; private set; }

    public async Task OpenProjectAsync(string selectedRoot)
    {
        await SaveBeforeLeavingAsync();
        var loadTimer = System.Diagnostics.Stopwatch.StartNew();
        LastOpenUsedCache = false;
        CancelScan();
        ResetProjectResults();

        ModProjectContext context;
        try
        {
            context = _detector.Detect(selectedRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            ProjectName = "无法打开项目";
            ProjectPath = selectedRoot;
            ProjectSummary = exception.Message;
            StatusText = "项目路径无效";
            RecordModProblem("无法打开 Mod 路径", exception.Message, exception, selectedRoot);
            return;
        }

        _currentContext = context;
        OnPropertyChanged(nameof(CanRunGenerate));
        OnPropertyChanged(nameof(CanRunDevMode));
        OnPropertyChanged(nameof(CanRunUpload));
        OnPropertyChanged(nameof(HasOpenProject));
        OnPropertyChanged(nameof(GenerateAvailability));
        OnPropertyChanged(nameof(DevModeAvailability));
        OnPropertyChanged(nameof(UploadAvailability));
        ProjectPath = context.Layout.RootPath;
        ProjectName = new DirectoryInfo(context.Layout.RootPath).Name;
        ProjectSummary = context.Summary;
        foreach (var capability in context.Modules)
        {
            Modules.Add(new ModuleItemViewModel(capability));
        }

        AddProjectDiagnostics(context);
        if (!context.IsRecognized)
        {
            StatusText = context.Summary;
            RecordModProblem("无法识别 Mod", context.Summary, location: context.Layout.RootPath);
            return;
        }

        try
        {
            await _recentProjectStore.AddAsync(context.Layout.RootPath);
            ReplaceRecentProjects(await _recentProjectStore.LoadAsync());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Add(new DiagnosticItemViewModel(
                "最近项目",
                $"无法保存最近项目记录，但不影响只读扫描：{exception.Message}"));
            RecordToolProblem("无法保存最近项目", exception, context.Layout.RootPath);
        }

        var scanCancellation = new CancellationTokenSource();
        _scanCancellation = scanCancellation;
        IsScanning = true;
        ScanPercent = 0;
        StatusText = "准备扫描只读数据";
        var progress = new Progress<IndexProgress>(item =>
        {
            if (_scanCancellation == scanCancellation)
            {
                ScanPercent = item.Percent;
                StatusText = item.Message;
            }
        });

        try
        {
            if (PrepareNamesAsync is not null)
            {
                StatusText = "正在加载原版名称…";
                await PrepareNamesAsync();
                scanCancellation.Token.ThrowIfCancellationRequested();
            }
            var loadCache = await Task.Run(() => OpenLoadCache?.Invoke(context.Layout.RootPath), scanCancellation.Token);
            var result = await _indexer.IndexAsync(
                context,
                progress,
                scanCancellation.Token, loadCache);
            if (_scanCancellation != scanCancellation)
            {
                return;
            }

            ApplyIndexResult(result);
            var unitCapability = result.Modules.FirstOrDefault(module => module.Key == "units");
            var weaponCapability = result.Modules.FirstOrDefault(module => module.Key == "weapons");
            var ammoCapability = result.Modules.FirstOrDefault(module => module.Key == "ammo");
            var divisionCapability = result.Modules.FirstOrDefault(module => module.Key == "divisions");
            if (unitCapability?.CanScan == true ||
                weaponCapability?.CanScan == true ||
                ammoCapability?.CanScan == true ||
                divisionCapability?.CanScan == true || result.Modules.Any(m => m.Key is "strategic" or "rules" && m.CanScan))
            {
                StatusText = "正在建立项目字段、名称与引用索引";
                var unitData = await _unitLoader.LoadAsync(context, result, scanCancellation.Token, loadCache);
                foreach (var diagnostic in unitData.Diagnostics)
                {
                    Diagnostics.Add(new DiagnosticItemViewModel("P2 单位工作区", diagnostic));
                    RecordModProblem("单位工作区", diagnostic, location: context.Layout.RootPath, severity: ProblemSeverity.Warning);
                }

                _draftStore = new DraftStore(context.Layout.RootPath);
                var draftLoad = await _draftStore.LoadAsync(scanCancellation.Token);
                if (draftLoad.Error is not null)
                {
                    Diagnostics.Add(new DiagnosticItemViewModel("草稿", draftLoad.Error));
                    RecordModProblem("草稿读取失败", draftLoad.Error, location: context.Layout.RootPath);
                }

                WeaponWorkspaceData? weaponData = null;
                if (weaponCapability?.CanScan == true || ammoCapability?.CanScan == true)
                {
                    StatusText = "正在建立 Weapon/Ammo 关系与共享影响索引";
                    weaponData = await _weaponLoader.LoadAsync(context, result, unitData, scanCancellation.Token, loadCache);
                    foreach (var diagnostic in weaponData.Diagnostics)
                    {
                        Diagnostics.Add(new DiagnosticItemViewModel("P4 Weapon/Ammo 工作区", diagnostic));
                        RecordModProblem("Weapon/Ammo 工作区", diagnostic, location: context.Layout.RootPath, severity: ProblemSeverity.Warning);
                    }
                }

                DivisionWorkspaceData? divisionData = null;
                if (divisionCapability?.CanScan == true && divisionCapability.Availability != ModuleAvailability.ParseError)
                {
                    StatusText = "正在建立战术师、单位池、默认 Deck 与费用索引";
                    divisionData = await _divisionLoader.LoadAsync(context, result, unitData, scanCancellation.Token);
                    foreach (var diagnostic in divisionData.Diagnostics)
                    {
                        Diagnostics.Add(new DiagnosticItemViewModel("P5 战术师工作区", diagnostic));
                        RecordModProblem("战术师工作区", diagnostic, location: context.Layout.RootPath, severity: ProblemSeverity.Warning);
                    }
                }

                _creationUnits=unitData;_creationWeapons=weaponData;_creationDivisions=divisionData;
                UnitWorkspace = new UnitWorkspaceViewModel(
                    unitData,
                    weaponData,
                    divisionData,
                    _draftStore,
                    draftLoad,
                    SetStatusText,
                    () => OpenProjectAsync(context.Layout.RootPath));
                if (weaponData is not null)
                {
                    if (weaponCapability?.CanScan == true && weaponData.Weapons.Count > 0)
                    {
                        WeaponWorkspace = new WeaponWorkspaceViewModel(weaponData, _draftStore, UnitWorkspace, SetStatusText);
                    }
                    if (ammoCapability?.CanScan == true && weaponData.Ammunition.Count > 0)
                    {
                        AmmoWorkspace = new AmmoWorkspaceViewModel(weaponData, _draftStore, UnitWorkspace, SetStatusText);
                    }
                }
                if (divisionData?.HasCompleteFileSet == true && divisionData.Divisions.Count > 0)
                {
                    DivisionWorkspace = new DivisionWorkspaceViewModel(divisionData, _draftStore, SetStatusText, UnitWorkspace.RefreshExternalDraftState);
                }

                if (result.Modules.Any(m => m.Key == "strategic" && m.CanScan))
                {
                    var strategic = await new WarnoLiteModdingTool.Core.Strategic.StrategicLoader().LoadAsync(context, result, unitData, scanCancellation.Token);
                    UnitWorkspace.StrategicData = strategic;
                    StrategicWorkspace = new StrategicWorkspaceViewModel(strategic, _draftStore, UnitWorkspace.RefreshExternalDraftState, SetStatusText);
                    UnitWorkspace.RefreshExternalDraftState();
                    OnPropertyChanged(nameof(StrategicWorkspace));
                    OnPropertyChanged(nameof(IsStrategicModule));
                OnPropertyChanged(nameof(IsRulesModule));
                    foreach (var diagnostic in strategic.Diagnostics) Diagnostics.Add(new DiagnosticItemViewModel("将军模式", diagnostic));
                }
                Modules.Add(new ModuleItemViewModel(new ModuleCapability(
                    "drafts",
                    "草稿总览",
                    ModuleAvailability.Available,
                    "查看当前项目全部未应用修改",
                    [],
                    [])));

                RulesWorkspace = new RulesWorkspaceViewModel(unitData.Rules!, _draftStore, UnitWorkspace.RefreshExternalDraftState);
                OnPropertyChanged(nameof(RulesWorkspace)); OnPropertyChanged(nameof(IsRulesModule));
                ProjectSummary = $"1.9.3 · Unit {UnitWorkspace.Units.Count:N0} · Weapon {weaponData?.Weapons.Count ?? 0:N0} · Ammo {weaponData?.Ammunition.Count ?? 0:N0} · Division {divisionData?.Divisions.Count ?? 0:N0} · Army General {StrategicWorkspace?.Data.Records.Count ?? 0:N0}";
            }

            RefreshMode();
            if (loadCache is not null && !result.Diagnostics.Any(d => d.Severity == NdfDiagnosticSeverity.Error))
                await Task.Run(() => loadCache.Save(scanCancellation.Token), scanCancellation.Token);
            LastOpenUsedCache = loadCache?.IsHit == true;
            LastOpenMilliseconds = loadTimer.ElapsedMilliseconds;
            ScanPercent = 100;
            StatusText = UnitWorkspace is null
                ? $"扫描完成 · {result.Objects.Count:N0} 个对象 · {result.Diagnostics.Count:N0} 条诊断"
                : $"扫描完成 · {UnitWorkspace.Units.Count:N0} 个 Unit · {UnitWorkspace.DraftCount:N0} 项草稿";
        }
        catch (OperationCanceledException)
        {
            if (_scanCancellation == scanCancellation)
            {
                StatusText = "扫描已取消";
            }
        }
        catch (Exception exception)
        {
            StatusText = "Mod 扫描失败；详情已记录到问题中心";
            RecordModProblem("Mod 扫描失败", exception.Message, exception, context.Layout.RootPath);
        }
        finally
        {
            if (_scanCancellation == scanCancellation)
            {
                IsScanning = false;
                _scanCancellation = null;
            }

            scanCancellation.Dispose();
        }
    }

    public void CancelScan() => _scanCancellation?.Cancel();

    public async Task RemoveSelectedRecentProjectAsync()
    {
        if (SelectedRecentProject is null)
        {
            return;
        }

        try
        {
            await _recentProjectStore.RemoveAsync(SelectedRecentProject.Path);
            ReplaceRecentProjects(await _recentProjectStore.LoadAsync());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText = $"无法移除最近项目记录：{exception.Message}";
            RecordToolProblem("无法移除最近项目记录", exception, SelectedRecentProject.Path);
        }
    }

    public Task<OfficialCommandResult> RunGenerateAsync() => RunOfficialCommandAsync("generate");

    public Task<OfficialCommandResult> RunDevModeAsync() => RunOfficialCommandAsync("dev");

    public Task<OfficialCommandResult> RunUploadAsync() => RunOfficialCommandAsync("upload");

    public string? SuggestedModsRoot()
    {
        var currentParent = _currentContext is null ? null : Directory.GetParent(_currentContext.Layout.RootPath)?.FullName;
        if (currentParent is not null && _modCreationService.Probe(currentParent).IsAvailable)
        {
            return currentParent;
        }

        var saved = _modsRootStore.Load();
        return saved is not null && _modCreationService.Probe(saved).IsAvailable ? saved : currentParent ?? saved;
    }

    public ModCreationCapability ProbeModCreation(string modsRoot) => _modCreationService.Probe(modsRoot);

    public string? ValidateModName(string modsRoot, string name) => _modCreationService.ValidateName(modsRoot, name);

    public async Task<ModCreationResult> CreateModAsync(string modsRoot, string name)
    {
        if (IsOfficialCommandBusy)
        {
            throw new InvalidOperationException("已有官方流程正在运行，请等待结束。");
        }

        IsOfficialCommandBusy = true;
        OfficialCommandOutput = string.Empty;
        StatusText = $"正在创建 Mod：{name}…";
        try
        {
            _modsRootStore.Save(modsRoot);
            var result = await _modCreationService.CreateAsync(modsRoot, name);
            OfficialCommandOutput = result.CommandResult.CombinedOutput.Length == 0
                ? "CreateNewMod.bat 已结束，新 Mod 结构验证通过。"
                : result.CommandResult.CombinedOutput;
            StatusText = $"已创建 Mod：{name}";
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RecordModProblem("创建 Mod 失败", exception.Message, exception, modsRoot);
            StatusText = "创建 Mod 失败；详情已记录到问题中心";
            throw;
        }
        finally
        {
            IsOfficialCommandBusy = false;
        }
    }

    public void OpenProblems()
    {
        SelectedModule = _problemModule;
        SelectedProblem ??= ToolProblems.LastOrDefault() ?? ModProblems.LastOrDefault();
    }

    public ProblemReport? ConsumeLastFatal()
    {
        var report = _problemLog.ConsumeLastFatal();
        if (report is not null)
        {
            AddProblem(report);
        }

        return report;
    }

    public void RecordToolProblem(string title, Exception exception, string? location = null) =>
        _problemLog.Record(ProblemSource.Tool, ProblemSeverity.Error, title, exception.Message, exception, location);

    public void RecordModProblem(
        string title,
        string summary,
        Exception? exception = null,
        string? location = null,
        string? details = null,
        ProblemSeverity severity = ProblemSeverity.Error) =>
        _problemLog.Record(ProblemSource.Mod, severity, title, summary, exception, location, details);

    public void Dispose()
    {
        _scanCancellation?.Cancel();
        DivisionWorkspace?.Dispose();
        _draftStore?.Dispose();
        _problemLog.Reported -= ProblemLog_Reported;
    }

    private void ResetProjectResults()
    {
        DivisionWorkspace?.Dispose();
        _draftStore?.Dispose();
        _draftStore = null;_creationUnits=null;_creationWeapons=null;_creationDivisions=null;
        _currentContext = null;
        OfficialCommandOutput = string.Empty;
        OnPropertyChanged(nameof(CanRunGenerate));
        OnPropertyChanged(nameof(CanRunDevMode));
        OnPropertyChanged(nameof(CanRunUpload));
        OnPropertyChanged(nameof(HasOpenProject));
        OnPropertyChanged(nameof(GenerateAvailability));
        OnPropertyChanged(nameof(DevModeAvailability));
        OnPropertyChanged(nameof(UploadAvailability));
        UnitWorkspace = null;
        WeaponWorkspace = null;
        AmmoWorkspace = null;
        DivisionWorkspace = null;
        RulesWorkspace = null;
        OnPropertyChanged(nameof(RulesWorkspace));
        StrategicWorkspace = null;
        OnPropertyChanged(nameof(StrategicWorkspace));
        OnPropertyChanged(nameof(IsStrategicModule));
                OnPropertyChanged(nameof(IsRulesModule));
        Modules.Clear();
        Modules.Add(_problemModule);
        Diagnostics.Clear();
        ModProblems.Clear();
        if (SelectedProblem?.Report.Source == ProblemSource.Mod)
        {
            SelectedProblem = ToolProblems.LastOrDefault();
        }
        OnPropertyChanged(nameof(ModProblemCount));
        OnPropertyChanged(nameof(ProblemCount));
        OnPropertyChanged(nameof(ProblemButtonText));
        SelectedModule = null;
        SelectedObject = null;
        SelectedDraft = null;
        ObjectsView = new ListCollectionView(new List<NdfObjectInfo>());
        VisibleObjectCount = 0;
        ScanPercent = 0;
    }

    private void SetStatusText(string message) => StatusText = message;

    private async Task<OfficialCommandResult> RunOfficialCommandAsync(string key)
    {
        var capability = _currentContext?.OfficialCommand(key);
        if (_currentContext is null || capability?.CommandPath is null)
        {
            throw new InvalidOperationException(capability?.AvailabilityReason ?? "当前项目没有可用的官方入口。");
        }

        if (IsOfficialCommandBusy)
        {
            throw new InvalidOperationException("已有官方流程正在运行，请等待结束。");
        }

        IsOfficialCommandBusy = true;
        StatusText = $"正在运行{capability.DisplayName}…";
        try
        {
            var result = await _officialCommandRunner.RunAsync(_currentContext.Layout.RootPath, capability.CommandPath);
            OfficialCommandOutput = result.CombinedOutput.Length == 0
                ? $"{capability.FileName} 已结束，退出码 {result.ExitCode}。"
                : result.CombinedOutput;
            StatusText = result.Succeeded
                ? $"{capability.DisplayName}流程已结束"
                : $"{capability.DisplayName}失败 · 退出码 {result.ExitCode}";
            if (!result.Succeeded)
            {
                RecordModProblem(
                    $"{capability.DisplayName}失败",
                    $"{capability.FileName} 退出码 {result.ExitCode}",
                    null,
                    capability.CommandPath,
                    result.CombinedOutput);
            }

            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RecordToolProblem($"无法运行{capability.DisplayName}", exception, capability.CommandPath);
            throw;
        }
        finally
        {
            IsOfficialCommandBusy = false;
        }
    }

    private void AddProjectDiagnostics(ModProjectContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            Diagnostics.Add(new DiagnosticItemViewModel("项目探测", diagnostic));
            RecordModProblem("项目探测", diagnostic, location: context.Layout.RootPath, severity: ProblemSeverity.Warning);
        }

        if (context.LocalisationDictionaries.Count == 0)
        {
            Diagnostics.Add(new DiagnosticItemViewModel("本地化", "未发现 LocalisationDicos.ndf；其他可用模块仍可浏览。"));
        }
        else
        {
            Diagnostics.Add(new DiagnosticItemViewModel(
                "本地化",
                $"发现 {context.LocalisationDictionaries.Count} 个字典声明。"));
        }

        foreach (var command in context.OfficialCommands)
        {
            Diagnostics.Add(new DiagnosticItemViewModel(
                command.DisplayName,
                command.IsAvailable ? command.CommandPath! : command.AvailabilityReason));
        }
    }

    private bool CanRunOfficial(string key) =>
        _currentContext?.OfficialCommand(key)?.IsAvailable == true && !IsScanning && !IsOfficialCommandBusy;

    private string OfficialAvailability(string key) =>
        _currentContext?.OfficialCommand(key)?.AvailabilityReason ?? "请先打开 WARNO Mod";

    private void ProblemLog_Reported(ProblemReport report)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(() => AddProblem(report));
            return;
        }

        AddProblem(report);
    }

    private void AddProblem(ProblemReport report)
    {
        if(IgnoredProblems.Any(p=>p.Id==report.Id))return;
        var item = new ProblemItemViewModel(report);
        if (report.Source == ProblemSource.Mod)
        {
            ModProblems.Add(item);
            OnPropertyChanged(nameof(ModProblemCount));
        }
        else
        {
            ToolProblems.Add(item);
            OnPropertyChanged(nameof(ToolProblemCount));
        }

        SelectedProblem = item;
        OnPropertyChanged(nameof(ProblemCount));
        OnPropertyChanged(nameof(ProblemButtonText));
    }

    private void ApplyIndexResult(ProjectIndexResult result)
    {
        foreach (var capability in result.Modules)
        {
            Modules.First(item => item.Key == capability.Key).Update(capability);
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            var relativePath = Path.GetRelativePath(ProjectPath, diagnostic.SourceFile);
            Diagnostics.Add(new DiagnosticItemViewModel(
                $"{diagnostic.Severity} · {relativePath}:{diagnostic.LineNumber}",
                diagnostic.Message));
            RecordModProblem(
                "NDF 解析问题",
                diagnostic.Message,
                location: $"{relativePath}:{diagnostic.LineNumber}",
                severity: diagnostic.Severity == NdfDiagnosticSeverity.Error ? ProblemSeverity.Error : ProblemSeverity.Warning);
        }

        var view = new ListCollectionView(result.Objects.ToList());
        view.SortDescriptions.Add(new SortDescription(nameof(NdfObjectInfo.RelativeSourceFile), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(NdfObjectInfo.LineNumber), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(NdfObjectInfo.CharacterOffset), ListSortDirection.Ascending));
        ObjectsView = view;
        SelectedModule = Modules.FirstOrDefault(module => module.Key != "problems" && module.CanBrowse) ?? _problemModule;
        RefreshFilter();
    }

    private void RefreshFilter()
    {
        ObjectsView.Filter = item =>
        {
            if (item is not NdfObjectInfo descriptor)
            {
                return false;
            }

            if (SelectedModule is not null && descriptor.ModuleKey != SelectedModule.Key)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }

            return descriptor.DisplayName.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
                   descriptor.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                   descriptor.TypeName.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        };
        ObjectsView.Refresh();
        VisibleObjectCount = ObjectsView.Cast<object>().Count();
    }

    private void ReplaceRecentProjects(IReadOnlyList<RecentProjectEntry> projects)
    {
        var selectedPath = SelectedRecentProject?.Path;
        RecentProjects.Clear();
        foreach (var project in projects.OrderByDescending(item => item.LastOpenedUtc))
        {
            RecentProjects.Add(project);
        }

        SelectedRecentProject = RecentProjects.FirstOrDefault(item =>
            string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? RecentProjects.FirstOrDefault();
    }
}
