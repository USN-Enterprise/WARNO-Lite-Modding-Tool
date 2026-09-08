namespace WarnoLiteModdingTool.Core.Projects;

public sealed record OfficialCommandCapability(
    string Key,
    string DisplayName,
    string FileName,
    string? CommandPath,
    string AvailabilityReason)
{
    public bool IsAvailable => CommandPath is not null;
}
