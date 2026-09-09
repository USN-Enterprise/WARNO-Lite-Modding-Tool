using MessageBox = WarnoLiteModdingTool.App.Localisation.LocalizedMessageBox;
using System.Windows;
using System.Windows.Threading;
using WarnoLiteModdingTool.App.Diagnostics;
using WarnoLiteModdingTool.App.Theming;

namespace WarnoLiteModdingTool.App;

public partial class App : Application
{
    private SingleInstanceGate? _singleInstance;
    private void DataGrid_Loaded(object sender, RoutedEventArgs e) =>
        Controls.ResponsiveColumns.SetEnabled((DependencyObject)sender, true);

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = SingleInstanceGate.TryAcquire();
        if (_singleInstance is null)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Localisation.UiText.Current.SetLanguage(new Settings.UiSettings().Load().Language);
            MessageBox.Show("WARNO Lite Modding Tool 已在运行。", "已在运行", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        ThemeManager.Initialize();
        Localisation.UiText.Current.SetLanguage(new Settings.UiSettings().Load().Language);
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { base.OnExit(e); }
        finally { _singleInstance?.Dispose(); }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var report = ApplicationProblemLog.Current.Record(
            ProblemSource.Tool,
            ProblemSeverity.Fatal,
            "界面发生未处理异常",
            e.Exception.Message,
            e.Exception);
        MessageBox.Show(
            $"WARNO Lite Modding Tool 遇到致命错误并将退出。\n\n错误编号：{report.Id}\n日志目录：{ApplicationProblemLog.Current.LogRoot}",
            "工具错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = false;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString() ?? "未知进程异常");
        ApplicationProblemLog.Current.Record(
            ProblemSource.Tool,
            e.IsTerminating ? ProblemSeverity.Fatal : ProblemSeverity.Error,
            "进程发生未处理异常",
            exception.Message,
            exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ApplicationProblemLog.Current.Record(
            ProblemSource.Tool,
            ProblemSeverity.Error,
            "后台任务发生未观察异常",
            e.Exception.Message,
            e.Exception);
        e.SetObserved();
    }
}
