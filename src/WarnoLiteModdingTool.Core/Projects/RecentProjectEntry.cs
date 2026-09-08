using System.Text.Json.Serialization;

namespace WarnoLiteModdingTool.Core.Projects;

public sealed record RecentProjectEntry(string Path, DateTimeOffset LastOpenedUtc)
{
    [JsonIgnore]
    public bool Exists => Directory.Exists(Path);

    [JsonIgnore]
    public string DisplayText => Exists ? Path : $"{Path}（路径已失效）";
}

