namespace WarnoLiteModdingTool.Core.Projects;

public sealed record ModProjectLayout(
    string RootPath,
    string GameDataPath,
    string GameplayPath,
    string LocalisationPath);

