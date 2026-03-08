using Microsoft.UI.Xaml;
using System.Text;

namespace ThumbnailGridStudio.WinUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        TryWriteCrashLog("UI UnhandledException", e.Exception);
    }

    private void OnCurrentDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        TryWriteCrashLog("AppDomain UnhandledException", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryWriteCrashLog("TaskScheduler UnobservedTaskException", e.Exception);
    }

    private static void TryWriteCrashLog(string source, Exception? exception)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "startup-crash.log");
            var builder = new StringBuilder();
            builder.AppendLine($"[{DateTimeOffset.Now:O}] {source}");
            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }
            else
            {
                builder.AppendLine("No exception payload.");
            }
            builder.AppendLine();
            File.AppendAllText(path, builder.ToString());
        }
        catch
        {
            // Ignore logging failures.
        }
    }
}
