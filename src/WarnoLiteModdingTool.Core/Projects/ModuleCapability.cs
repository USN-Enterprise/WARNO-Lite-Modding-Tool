namespace WarnoLiteModdingTool.Core.Projects;

public sealed record ModuleCapability(
    string Key,
    string DisplayName,
    ModuleAvailability Availability,
    string Summary,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> MissingFiles)
{
    public bool CanScan => Availability is ModuleAvailability.Available or ModuleAvailability.Limited;
}

