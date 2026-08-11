using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SafeShutdown;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "Local\\SafeShutdown.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("SafeShutdown 已经在运行。", "SafeShutdown", MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogHelper.Error("界面线程发生未处理异常，程序将退出以避免处于不确定状态。", e.Exception);
        MessageBox.Show(
            $"程序遇到无法恢复的错误：{e.Exception.Message}{Environment.NewLine}详情已写入日志。",
            "SafeShutdown",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogHelper.Error("后台任务发生未观察异常。", e.Exception);
        e.SetObserved();
    }
}
