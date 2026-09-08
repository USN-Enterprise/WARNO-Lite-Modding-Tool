using System.IO;
using System.Text;

namespace WarnoLiteModdingTool.App.Theming;

public sealed class UiThemeStore
{
    private readonly string _settingsFile;

    public UiThemeStore(string? settingsFile = null)
    {
        _settingsFile = settingsFile ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WarnoLiteModdingTool",
            "theme.txt");
    }

    public AppTheme Load()
    {
        try
        {
            if (File.Exists(_settingsFile) &&
                Enum.TryParse<AppTheme>(File.ReadAllText(_settingsFile).Trim(), true, out var theme) &&
                Enum.IsDefined(theme))
            {
                return theme;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return AppTheme.DarkBlue;
    }

    public void Save(AppTheme theme)
    {
        var temporaryFile = _settingsFile + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(_settingsFile)
                ?? throw new InvalidOperationException("主题设置路径没有父目录。");
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporaryFile, theme.ToString(), new UTF8Encoding(false));
            File.Move(temporaryFile, _settingsFile, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (File.Exists(temporaryFile))
                {
                    File.Delete(temporaryFile);
                }
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
