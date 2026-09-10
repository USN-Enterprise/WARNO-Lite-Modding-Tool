using System.IO;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.Core.Localisation;

namespace WarnoLiteModdingTool.App.Settings;

public static class LocalGameNames
{
    private static readonly SemaphoreSlim Gate = new(1);
    public static bool ForceRefresh { get; set; }
    public static string Status { get; private set; } = "打开项目时自动加载原版名称";
    public static async Task PrepareAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var preferences = new UiSettings().Load();
            var force = ForceRefresh; ForceRefresh = false;
            var names = await Task.Run(() =>
            {
                var game = preferences.GameDirectory;
                if (string.IsNullOrWhiteSpace(game))
                    game = ModFinder.FindRoots(null, CancellationToken.None).Select(Path.GetDirectoryName)
                        .FirstOrDefault(p => p is not null && Directory.Exists(Path.Combine(p, "Data", "PC")));
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WarnoLiteModdingTool", "names", "cache-v1.json");
                return new GameNameCache(path).Load(game, force);
            });
            VanillaNames.Replace(names.Names);
            Status = "原版名称已加载";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or OverflowException)
        {
            VanillaNames.Replace(new());
            Status = "原版名称不可用，请在设置中选择游戏目录";
            Diagnostics.ApplicationProblemLog.Current.Record(Diagnostics.ProblemSource.Tool, Diagnostics.ProblemSeverity.Warning,
                Status, ex.Message, ex);
        }
        finally { Gate.Release(); }
    }
}
