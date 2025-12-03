using System.Windows;
using DCM.Core.Logging;

namespace DCM.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Globalen Exception-Handler registrieren
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        AppLogger.Instance.Info("App.OnStartup abgeschlossen", "App");
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Instance.Error(
            $"Unbehandelte UI-Exception: {e.Exception.Message}",
            "App",
            e.Exception);

        e.Handled = true;

        MessageBox.Show(
            $"Ein unerwarteter Fehler ist aufgetreten:\n\n{e.Exception.Message}\n\nDetails im Log.",
            "Fehler",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            AppLogger.Instance.Error(
                $"Fatale Exception: {ex.Message}",
                "App",
                ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Instance.Error(
            $"Unbeobachtete Task-Exception: {e.Exception.Message}",
            "App",
            e.Exception);

        e.SetObserved();
    }
}