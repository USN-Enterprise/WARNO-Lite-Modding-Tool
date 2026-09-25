using System.IO;
using WarnoLiteModdingTool.App.Controls;
using WarnoLiteModdingTool.Core.Localisation;

namespace WarnoLiteModdingTool.App.Settings;

public static class DivisionTextCache
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static async Task<string> PrepareAsync(bool force, CancellationToken cancellation, IProgress<string>? progress = null)
    {
        await Gate.WaitAsync(cancellation);
        try
        {
            var configured = new UiSettings().Load().GameDirectory;
            return await Task.Run(() =>
            {
                var game = configured;
                if (string.IsNullOrWhiteSpace(game)) game = ModFinder.FindRoots(null, cancellation).Select(Path.GetDirectoryName)
                    .FirstOrDefault(p => p is not null && Directory.Exists(Path.Combine(p, "Data", "PC")));
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WarnoLiteModdingTool", "names", "division-text-v1.json");
                var cache = new GameNameCache(path, unitsOnly: true);
                var result = cache.Load(game, force, cancellation, p => progress?.Report("正在读取：" + p));
                cancellation.ThrowIfCancellationRequested(); VanillaNames.ReplaceUnits(result.Names);
                return (cache.Offline ? "离线缓存，未核对游戏更新" : "原版简介已提取") + "\n" + result.Root + "\n" +
                    string.Join(" · ", result.Names.Where(k => k.Key.EndsWith("/UNITS")).Select(k => k.Key + ": " + k.Value.Count)) + "\n" + result.ExtractedUtc.ToLocalTime().ToString("g");
            }, cancellation);
        }
        finally { Gate.Release(); }
    }
}
