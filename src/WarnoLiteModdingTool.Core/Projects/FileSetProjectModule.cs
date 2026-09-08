namespace WarnoLiteModdingTool.Core.Projects;

public sealed class FileSetProjectModule(
    string key,
    string displayName,
    IReadOnlyList<string> relativeFiles,
    int minimumFileCount = 1) : IProjectModule
{
    public string Key { get; } = key;

    public string DisplayName { get; } = displayName;

    public ModuleCapability Probe(ModProjectLayout layout)
    {
        var candidates = relativeFiles
            .Select(relativePath => Path.GetFullPath(Path.Combine(layout.GameplayPath, relativePath)))
            .ToArray();
        var existing = candidates.Where(File.Exists).ToArray();
        var missing = candidates.Where(path => !File.Exists(path)).ToArray();

        if (existing.Length < minimumFileCount)
        {
            return new ModuleCapability(
                Key,
                DisplayName,
                ModuleAvailability.Unavailable,
                $"缺少必要文件（找到 {existing.Length}/{minimumFileCount}）",
                existing,
                missing);
        }

        var availability = missing.Length == 0
            ? ModuleAvailability.Available
            : ModuleAvailability.Limited;
        var summary = availability == ModuleAvailability.Available
            ? $"可用 · {existing.Length} 个源文件"
            : $"部分可用 · 找到 {existing.Length}/{candidates.Length} 个源文件";

        return new ModuleCapability(
            Key,
            DisplayName,
            availability,
            summary,
            existing,
            missing);
    }
}

