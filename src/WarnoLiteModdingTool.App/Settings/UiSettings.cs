using System.IO;
using System.Text.Json;

namespace WarnoLiteModdingTool.App.Settings;

public sealed record UiPreferences(string Language = "system", bool AdvancedMode = false, string? BackgroundImage = null, bool BackgroundEnabled = true, double BackgroundOpacity = 0.10, string BackgroundLayout = "fill");
public sealed class UiSettings(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WarnoLiteModdingTool", "settings.json");
    public UiPreferences Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(_path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(UiPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences), new System.Text.UTF8Encoding(false));
        File.Move(temporary, _path, true);
    }
}
