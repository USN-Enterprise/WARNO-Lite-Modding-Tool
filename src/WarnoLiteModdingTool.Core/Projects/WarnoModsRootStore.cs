using System.Text;

namespace WarnoLiteModdingTool.Core.Projects;

public sealed class WarnoModsRootStore
{
    private readonly string _settingsFile;

    public WarnoModsRootStore(string? settingsFile = null)
    {
        _settingsFile = settingsFile ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WarnoLiteModdingTool",
            "mods-root.txt");
    }

    public string? Load()
    {
        try
        {
            return File.Exists(_settingsFile) ? File.ReadAllText(_settingsFile).Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string modsRoot)
    {
        var directory = Path.GetDirectoryName(_settingsFile)
            ?? throw new InvalidOperationException("WARNO Mods 设置路径没有父目录。");
        Directory.CreateDirectory(directory);
        File.WriteAllText(_settingsFile, Path.GetFullPath(modsRoot), new UTF8Encoding(false));
    }
}
