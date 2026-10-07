using System.Text.Json;
using WarnoLiteModdingTool.Core.Changes;

namespace WarnoLiteModdingTool.Core.SourceWorkspace;

public sealed record SourceTabState(string Path, int Offset = 0, double Vertical = 0, double FontSize = 14);
public sealed record SourceSession(IReadOnlyList<SourceTabState> Tabs, string? Active, double TreeWidth = 235, double DetailWidth = 260, double OutputHeight = 160)
{
    private const string Name = ".warno-editor/source-session.json";
    public static SourceSession Load(string root)
    {
        try { var path = ChangePaths.Resolve(root, Name); var value = File.Exists(path) ? JsonSerializer.Deserialize<SourceSession>(File.ReadAllBytes(path)) : null; return value is { Tabs: not null } ? value with { Tabs = value.Tabs.Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Path)).ToArray() } : new([],null); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { return new([], null); }
    }
    public void Save(string root)
    {
        var path = ChangePaths.Resolve(root, Name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(this)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
