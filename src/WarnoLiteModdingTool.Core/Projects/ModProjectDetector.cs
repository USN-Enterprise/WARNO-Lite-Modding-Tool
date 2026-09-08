namespace WarnoLiteModdingTool.Core.Projects;

public sealed class ModProjectDetector
{
    private readonly IReadOnlyList<IProjectModule> _modules;

    public ModProjectDetector(IReadOnlyList<IProjectModule>? modules = null)
    {
        _modules = modules ?? ProjectModuleCatalog.CreateDefault();
    }

    public ModProjectContext Detect(string selectedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedRoot);

        var rootPath = Path.GetFullPath(selectedRoot.Trim());
        var layout = new ModProjectLayout(
            rootPath,
            Path.Combine(rootPath, "GameData"),
            Path.Combine(rootPath, "GameData", "Generated", "Gameplay"),
            Path.Combine(rootPath, "GameData", "Localisation"));

        if (!Directory.Exists(rootPath))
        {
            return Unrecognized(layout, "所选文件夹不存在。");
        }

        if (!Directory.Exists(layout.GameDataPath))
        {
            return Unrecognized(layout, "未找到 GameData，无法识别为 WARNO Mod 根目录。");
        }

        var diagnostics = new List<string>();
        var dictionaries = DiscoverLocalisationDictionaries(layout.LocalisationPath, diagnostics);
        var capabilities = _modules.Select(module => module.Probe(layout)).ToArray();
        var officialCommands = ProbeOfficialCommands(layout);
        var availableCount = capabilities.Count(capability => capability.CanScan);

        return new ModProjectContext(
            layout,
            true,
            $"已识别 · {availableCount}/{capabilities.Length} 个只读模块可用",
            capabilities,
            dictionaries,
            officialCommands,
            diagnostics);
    }

    private ModProjectContext Unrecognized(ModProjectLayout layout, string summary)
    {
        var unavailable = _modules.Select(module => new ModuleCapability(
            module.Key,
            module.DisplayName,
            ModuleAvailability.Unavailable,
            summary,
            [],
            [])).ToArray();

        return new ModProjectContext(
            layout,
            false,
            summary,
            unavailable,
            [],
            [],
            []);
    }

    private static IReadOnlyList<string> DiscoverLocalisationDictionaries(
        string localisationPath,
        ICollection<string> diagnostics)
    {
        if (!Directory.Exists(localisationPath))
        {
            return [];
        }

        try
        {
            return Directory
                .EnumerateFiles(localisationPath, "LocalisationDicos.ndf", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add($"本地化目录扫描失败：{exception.Message}");
            return [];
        }
    }

    private static IReadOnlyList<OfficialCommandCapability> ProbeOfficialCommands(ModProjectLayout layout)
    {
        var modsRoot = Directory.GetParent(layout.RootPath)?.FullName;
        var gameRoot = modsRoot is null ? null : Directory.GetParent(modsRoot)?.FullName;
        var python = modsRoot is null ? null : Path.Combine(modsRoot, "Utils", "Python", "python.exe");
        var scripts = modsRoot is null ? null : Path.Combine(modsRoot, "Utils", "Scripts");
        var game = gameRoot is null ? null : Path.Combine(gameRoot, "WARNO.exe");
        return
        [
            ProbeOfficialCommand(
                layout.RootPath,
                "generate",
                "生成/编译 Mod",
                "GenerateMod.bat",
                [(python, "WARNO Mod 工具 Python"), (scripts is null ? null : Path.Combine(scripts, "GenerateMod.py"), "GenerateMod.py")]),
            ProbeOfficialCommand(
                layout.RootPath,
                "dev",
                "启动开发模式",
                "LaunchGameDevMode.bat",
                [(game, "WARNO.exe")]),
            ProbeOfficialCommand(
                layout.RootPath,
                "upload",
                "上传 Mod",
                "UploadMod.bat",
                [
                    (game, "WARNO.exe"),
                    (python, "WARNO Mod 工具 Python"),
                    (scripts is null ? null : Path.Combine(scripts, "CreateModBackup.py"), "CreateModBackup.py")
                ])
        ];
    }

    private static OfficialCommandCapability ProbeOfficialCommand(
        string projectRoot,
        string key,
        string displayName,
        string fileName,
        IReadOnlyList<(string? Path, string Label)> dependencies)
    {
        var commandPath = Path.Combine(projectRoot, fileName);
        if (!File.Exists(commandPath))
        {
            return new OfficialCommandCapability(key, displayName, fileName, null, $"未发现 {fileName}");
        }

        var missing = dependencies
            .Where(item => item.Path is null || !File.Exists(item.Path))
            .Select(item => item.Label)
            .ToArray();
        if (missing.Length > 0)
        {
            return new OfficialCommandCapability(key, displayName, fileName, null, $"缺少：{string.Join("、", missing)}");
        }

        return new OfficialCommandCapability(key, displayName, fileName, Path.GetFullPath(commandPath), "可用");
    }
}
