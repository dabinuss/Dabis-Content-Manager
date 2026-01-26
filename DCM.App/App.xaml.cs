using System;
using System.Threading.Tasks;
using System.Windows;
using DCM.Core.Configuration;
using DCM.Core.Logging;

namespace DCM.App;

public partial class App : Application
{
    private const string AppLogSource = "App";

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

        AppLogger.Instance.Info(LocalizationHelper.Get("Log.App.StartupCompleted"), AppLogSource);
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
            LocalizationHelper.Format("Log.App.UnhandledUiException", e.Exception.Message),
            AppLogSource,
            e.Exception);

        e.Handled = true;

        MessageBox.Show(
            LocalizationHelper.Format("Dialog.Error.Unhandled.Text", e.Exception.Message),
            LocalizationHelper.Get("Dialog.Error.Unhandled.Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            AppLogger.Instance.Error(
                LocalizationHelper.Format("Log.App.FatalException", ex.Message),
                AppLogSource,
                ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Instance.Error(
            LocalizationHelper.Format("Log.App.UnobservedTaskException", e.Exception.Message),
            AppLogSource,
            e.Exception);

        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        base.OnExit(e);
        AppLogger.Instance.Dispose();
    }
}
