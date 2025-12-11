using System;
using System.Threading.Tasks;
using System.Windows;
using DCM.Core.Configuration;
using DCM.Core.Logging;

namespace DCM.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Globale Exception-Handler registrieren
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Sprache initialisieren, bevor das MainWindow erstellt wird
        InitializeLocalization();

        // MainWindow manuell erzeugen
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        AppLogger.Instance.Info("App.OnStartup abgeschlossen", "App");
    }

    private void InitializeLocalization()
    {
        try
        {
            var settingsProvider = new JsonSettingsProvider(AppLogger.Instance);
            var settings = settingsProvider.Load();

            LocalizationManager.Instance.Initialize(settings.Language);
        }
        catch
        {
            // Fallback auf Deutsch bei Fehler
            LocalizationManager.Instance.Initialize("de-DE");
        }
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

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        AppLogger.Instance.Dispose();
    }
}
