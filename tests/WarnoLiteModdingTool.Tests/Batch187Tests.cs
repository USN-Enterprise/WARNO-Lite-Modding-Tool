using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WarnoLiteModdingTool.App;

namespace WarnoLiteModdingTool.Tests;
internal static partial class Program
{
    private static int SingleInstanceProbe(string name, string mode)
    {
        using var gate = SingleInstanceGate.TryAcquire(name);
        Console.WriteLine(gate is null ? "blocked" : "acquired");
        Console.Out.Flush();
        if (gate is not null && mode == "hold") Console.ReadLine();
        return gate is null ? 2 : 0;
    }

    private static int DuplicateStartupProbe()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            WarnoLiteModdingTool.App.App? app = null;
            try
            {
                app = new WarnoLiteModdingTool.App.App();
                app.InitializeComponent();
                var found = false;
                var start = DateTime.UtcNow;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                timer.Tick += (_, _) =>
                {
                    try
                    {
                        Assert(!app.Windows.OfType<MainWindow>().Any(), "重复启动不得创建主窗口");
                        var popup = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible && w.Title is "已在运行" or "Already running");
                        if (popup is not null)
                        {
                            Assert(FindVisualChildren<TextBlock>(popup).Any(t => t.Text is "WARNO Lite Modding Tool 已在运行。" or "WARNO Lite Modding Tool is already running."), "实际弹窗明确提示已在运行");
                            var directory = Path.GetFullPath("publish/qa-1.8.7");
                            Directory.CreateDirectory(directory);
                            popup.UpdateLayout();
                            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(popup.ActualWidth), (int)Math.Ceiling(popup.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                            bitmap.Render(popup);
                            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                            using (var stream = File.Create(Path.Combine(directory, "187-already-running.png"))) encoder.Save(stream);
                            found = true;
                            timer.Stop();
                            popup.Close();
                        }
                        else if (DateTime.UtcNow - start > TimeSpan.FromSeconds(5)) throw new TimeoutException("未出现已在运行弹窗");
                    }
                    catch (Exception ex) { error = ex; timer.Stop(); app.Shutdown(); }
                };
                timer.Start();
                app.Run();
                Assert(found && error is null, error?.ToString() ?? "未验证重复运行弹窗");
            }
            catch (Exception ex) { error = ex; app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(10))) return 3;
        if (error is not null) { Console.Error.WriteLine(error); return 1; }
        Console.WriteLine("popup passed; no main window; exited");
        return 0;
    }

    private static Process Start187Probe(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }

    private static async Task SingleInstance187()
    {
        var name = @"Local\WarnoLiteModdingTool.Test." + Guid.NewGuid().ToString("N");
        using var first = Start187Probe("--single-instance-probe", name, "hold");
        try
        {
            Assert(await first.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) == "acquired", "首进程取得单实例所有权");
            using (var second = Start187Probe("--single-instance-probe", name, "try"))
            {
                await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert(second.ExitCode == 2, "独立进程重复启动被拒绝");
            }
            await first.StandardInput.WriteLineAsync("exit");
            await first.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using var next = Start187Probe("--single-instance-probe", name, "hold");
            try
            {
                Assert(await next.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) == "acquired", "正常退出后可重开");
                // Keep a non-owning handle so recovery exercises an abandoned mutex, not only recreation.
                using var observer = Mutex.OpenExisting(name);
                next.Kill();
                await next.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                using var recovery = Start187Probe("--single-instance-probe", name, "try");
                await recovery.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert(recovery.ExitCode == 0, "异常退出后的废弃互斥量可重新取得");
            }
            finally { if (!next.HasExited) next.Kill(); }
        }
        finally { if (!first.HasExited) first.Kill(); }

        using var owner = Start187Probe("--single-instance-probe", SingleInstanceGate.Name, "hold");
        try
        {
            var status = await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert(status is "acquired" or "blocked", "实际启动互斥量存在");
            using var duplicate = Start187Probe("--duplicate-startup-probe");
            await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));
            Assert(duplicate.ExitCode == 0, "重复启动实际App只显示弹窗并退出：" + await duplicate.StandardError.ReadToEndAsync());
        }
        finally
        {
            if (!owner.HasExited)
            {
                await owner.StandardInput.WriteLineAsync("exit");
                await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }
}
