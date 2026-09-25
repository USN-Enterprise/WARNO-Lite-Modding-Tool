namespace WarnoLiteModdingTool.Core.Projects;
public sealed class RuleProjectModule : IProjectModule
{
    public string Key => "rules";
    public string DisplayName => "游戏规则";
    public ModuleCapability Probe(ModProjectLayout layout)
    {
        var paths = Rules.RuleCatalog.All.Select(d => Path.Combine(layout.RootPath,d.RelativePath)).Distinct().ToArray();
        var found = paths.Where(File.Exists).ToArray();
        var hasExperience = Directory.Exists(layout.GameDataPath) && Directory.EnumerateFiles(layout.GameDataPath, "*.ndf", SearchOption.AllDirectories)
            .Any(p => { try { var text = ProjectReadScope.ReadAllText(p); return text.Contains("TExperienceLevelsPackDescriptor", StringComparison.Ordinal) || text.Contains("TGameplayTerrainsRegistration", StringComparison.Ordinal); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; } });
        return new(Key,DisplayName,found.Length == 0 && !hasExperience ? ModuleAvailability.Unavailable : found.Length == paths.Length ? ModuleAvailability.Available : ModuleAvailability.Limited,
            "编辑当前 Mod 的游戏规则",found,paths.Except(found).ToArray());
    }
}
