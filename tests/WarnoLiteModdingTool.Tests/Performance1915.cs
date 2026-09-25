using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Diagnostics;
using WarnoLiteModdingTool.App.ViewModels;
using WarnoLiteModdingTool.Core.Projects;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    // Explicit synthetic benchmark. The directory is created once and may be reused across processes.
    private static async Task Performance1915(string root, int count)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
        {
            CopyDirectory(Fixture("p2-unit-complete"), root);
            var file = Path.Combine(root, "GameData/Generated/Gameplay/Gfx/UniteDescriptor.ndf");
            var original = File.ReadAllText(file);
            var obj = Scanner.Scan(original, file, "units", root).Objects.First();
            var template = original.Substring(obj.CharacterOffset, obj.CharacterLength);
            File.WriteAllText(file, string.Join("\n", Enumerable.Range(0, count).Select(i => template.Replace(obj.Name, $"Descriptor_Unit_Perf_{i:D5}"))), new UTF8Encoding(false));
            if (root.Contains("experience", StringComparison.OrdinalIgnoreCase)) WriteExperience198(root);
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                var cachePath = Path.Combine(root, ".warno-editor", "performance-cache.gz");
                using var main = new MainViewModel(new RecentProjectStore(Path.Combine(root, ".warno-editor", "recent.json")),
                    problemLog: new ApplicationProblemLog(Path.Combine(root, ".warno-editor", "problems")));
                foreach (var mode in new[] { "disabled", "cache", "repeat", "repeat", "repeat", "repeat", "repeat" })
                {
                    main.OpenLoadCache = p => ProjectLoadCache.Open(p, mode != "disabled", cachePath);
                    var timer = Stopwatch.StartNew();
                    RunWithDispatcher(main.OpenProjectAsync(root), dispatcher);
                    DrainDispatcher(dispatcher);
                    if (main.UnitWorkspace is null) throw new InvalidOperationException(main.StatusText);
                    Console.WriteLine(JsonSerializer.Serialize(new { mode, count = main.UnitWorkspace.Units.Count, milliseconds = timer.ElapsedMilliseconds,
                        hit = main.LastOpenUsedCache, phases = main.GetType().GetProperty("LastLoadTimings")?.GetValue(main), memory = GC.GetTotalMemory(false) }));
                    if (main.GetType().GetProperty("CacheSaveTask")?.GetValue(main) is Task save) RunWithDispatcher(save, dispatcher);
                }
                for (var i = 0; i < 5; i++)
                {
                    var workspace = main.UnitWorkspace!;
                    var field = workspace.Fields.Single(f => f.Key == "survival.health");
                    field.EditValue = (decimal.Parse(field.EditValue, System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var timer = Stopwatch.StartNew();
                    var prepared = workspace.PrepareApplyAsync(); RunWithDispatcher(prepared, dispatcher);
                    var previewMs = timer.ElapsedMilliseconds;
                    timer.Restart();
                    var commit = workspace.CommitApplyAsync(prepared.Result); RunWithDispatcher(commit, dispatcher); DrainDispatcher(dispatcher);
                    if (commit.Result.Warnings.Count != 0) throw new InvalidOperationException(string.Join(";", commit.Result.Warnings));
                    Console.WriteLine(JsonSerializer.Serialize(new { mode = "apply", count, previewMs, commitAndRefreshMs = timer.ElapsedMilliseconds,
                        refresh = main.GetType().GetProperty("LastRefreshTimings")?.GetValue(main),
                        readFiles = main.GetType().GetProperty("LastRefreshReadFiles")?.GetValue(main),
                        readBytes = main.GetType().GetProperty("LastRefreshReadBytes")?.GetValue(main) }));
                    if (main.GetType().GetProperty("CacheSaveTask")?.GetValue(main) is Task save) RunWithDispatcher(save, dispatcher);
                }
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }
}
