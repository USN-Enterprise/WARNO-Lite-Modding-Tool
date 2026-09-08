using System.Text.Json;

namespace WarnoLiteModdingTool.Core.Projects;

public sealed class RecentProjectStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFile;

    public RecentProjectStore(string? settingsFile = null)
    {
        _settingsFile = settingsFile ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WarnoLiteModdingTool",
            "recent-projects.json");
    }

    public async Task<IReadOnlyList<RecentProjectEntry>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFile))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_settingsFile);
            return await JsonSerializer.DeserializeAsync<List<RecentProjectEntry>>(
                       stream,
                       SerializerOptions,
                       cancellationToken) ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public async Task AddAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var normalized = Path.GetFullPath(projectPath);
        var current = await LoadAsync(cancellationToken);
        var updated = current
            .Where(item => !string.Equals(item.Path, normalized, StringComparison.OrdinalIgnoreCase))
            .Prepend(new RecentProjectEntry(normalized, DateTimeOffset.UtcNow))
            .Take(10)
            .ToArray();
        await SaveAsync(updated, cancellationToken);
    }

    public async Task RemoveAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var normalized = Path.GetFullPath(projectPath);
        var current = await LoadAsync(cancellationToken);
        var updated = current
            .Where(item => !string.Equals(item.Path, normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        await SaveAsync(updated, cancellationToken);
    }

    private async Task SaveAsync(
        IReadOnlyList<RecentProjectEntry> projects,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_settingsFile)
            ?? throw new InvalidOperationException("最近项目设置路径没有父目录。");
        Directory.CreateDirectory(directory);

        var temporaryFile = _settingsFile + ".tmp";
        await using (var stream = new FileStream(
                         temporaryFile,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                projects,
                SerializerOptions,
                cancellationToken);
        }

        File.Move(temporaryFile, _settingsFile, true);
    }
}

