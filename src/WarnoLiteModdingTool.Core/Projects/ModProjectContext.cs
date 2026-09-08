namespace WarnoLiteModdingTool.Core.Projects;

public sealed record ModProjectContext(
    ModProjectLayout Layout,
    bool IsRecognized,
    string Summary,
    IReadOnlyList<ModuleCapability> Modules,
    IReadOnlyList<string> LocalisationDictionaries,
    IReadOnlyList<OfficialCommandCapability> OfficialCommands,
    IReadOnlyList<string> Diagnostics)
{
    public OfficialCommandCapability? OfficialCommand(string key) =>
        OfficialCommands.FirstOrDefault(command => string.Equals(command.Key, key, StringComparison.Ordinal));
}
