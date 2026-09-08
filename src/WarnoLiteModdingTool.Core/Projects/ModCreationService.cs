using System.Text.RegularExpressions;
using WarnoLiteModdingTool.Core.Transactions;

namespace WarnoLiteModdingTool.Core.Projects;

public sealed record ModCreationCapability(
    string ModsRoot,
    string? CommandPath,
    string AvailabilityReason)
{
    public bool IsAvailable => CommandPath is not null;
}

public sealed record ModCreationResult(
    string ModRoot,
    OfficialCommandResult CommandResult);

public sealed class ModCreationService
{
    private static readonly Regex ValidName = new("^[A-Za-z0-9]{1,32}$", RegexOptions.CultureInvariant);
    private static readonly Regex ReservedName = new("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly OfficialCommandRunner _runner;

    public ModCreationService(OfficialCommandRunner? runner = null)
    {
        _runner = runner ?? new OfficialCommandRunner();
    }

    public ModCreationCapability Probe(string selectedRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedRoot);
        var root = Path.GetFullPath(selectedRoot.Trim()).TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(root))
        {
            return new ModCreationCapability(root, null, "所选 WARNO Mods 文件夹不存在。");
        }

        var required = new (string Path, string Label)[]
        {
            (Path.Combine(root, "CreateNewMod.bat"), "CreateNewMod.bat"),
            (Path.Combine(root, "Utils", "Python", "python.exe"), "Utils/Python/python.exe"),
            (Path.Combine(root, "Utils", "Scripts", "CreateNewMod.py"), "Utils/Scripts/CreateNewMod.py"),
            (Path.Combine(root, "ModData", "base.zip"), "ModData/base.zip")
        };
        var missing = required.Where(item => !File.Exists(item.Path)).Select(item => item.Label).ToArray();
        return missing.Length == 0
            ? new ModCreationCapability(root, Path.GetFullPath(required[0].Path), "可用")
            : new ModCreationCapability(root, null, $"缺少：{string.Join("、", missing)}");
    }

    public string? ValidateName(string modsRoot, string name)
    {
        if (!ValidName.IsMatch(name))
        {
            return "名称必须为 1–32 位英文字母或数字。";
        }

        if (ReservedName.IsMatch(name))
        {
            return "该名称是 Windows 保留名称，请更换。";
        }

        var root = Path.GetFullPath(modsRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (Directory.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
        {
            return "已经存在同名 Mod，请更换名称。";
        }

        return null;
    }

    public async Task<ModCreationResult> CreateAsync(
        string modsRoot,
        string name,
        CancellationToken cancellationToken = default)
    {
        var capability = Probe(modsRoot);
        if (!capability.IsAvailable)
        {
            throw new InvalidOperationException(capability.AvailabilityReason);
        }

        var nameError = ValidateName(capability.ModsRoot, name);
        if (nameError is not null)
        {
            throw new InvalidOperationException(nameError);
        }

        var target = Path.GetFullPath(Path.Combine(capability.ModsRoot, name));
        var expectedPrefix = capability.ModsRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("新 Mod 路径越出了所选 WARNO Mods 文件夹。");
        }

        var result = await _runner.RunAsync(capability.ModsRoot, capability.CommandPath!, name, cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"CreateNewMod.bat 失败，退出码 {result.ExitCode}。{Environment.NewLine}{result.CombinedOutput}".Trim());
        }

        if (!Directory.Exists(target) || !Directory.Exists(Path.Combine(target, "GameData")))
        {
            throw new InvalidOperationException("官方创建流程已结束，但没有生成包含 GameData 的目标 Mod。");
        }

        return new ModCreationResult(target, result);
    }
}
