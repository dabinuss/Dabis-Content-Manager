using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using DCM.Core;
using DCM.Core.Logging;

namespace DCM.App;

public partial class LogWindow : Window
{
    private readonly IAppLogger _logger;
    private readonly ObservableCollection<LogEntry> _filteredEntries = new();
    private bool _isInitialized;
    private bool _isClosing;

    public LogWindow()
    {
        // Logger ZUERST holen, bevor InitializeComponent
        _logger = AppLogger.Instance;

        InitializeComponent();

        // ItemsSource NACH InitializeComponent setzen
        LogListBox.ItemsSource = _filteredEntries;

        // Jetzt ist alles initialisiert
        _isInitialized = true;

        Loaded += OnWindowLoaded;
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;

        try
        {
            _logger.EntryAdded -= OnLogEntryAdded;
        }
        catch
        {
            // Ignorieren
        }

        base.OnClosed(e);
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Event-Handler erst hier registrieren
        _logger.EntryAdded += OnLogEntryAdded;
        RefreshEntries();
    }

    private void OnLogEntryAdded(LogEntry entry)
    {
        if (_isClosing || !_isInitialized)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_isClosing && _isInitialized)
                    {
                        AddEntryToList(entry);
                    }
                });
            }
            catch
            {
                // Dispatcher könnte bereits heruntergefahren sein
            }
            return;
        }

        AddEntryToList(entry);
    }

    private void AddEntryToList(LogEntry entry)
    {
        if (_isClosing || !_isInitialized)
        {
            return;
        }

        try
        {
            if (ShouldShowEntry(entry))
            {
                _filteredEntries.Add(entry);
                UpdateEntryCount();

                if (AutoScrollCheckBox?.IsChecked == true && _filteredEntries.Count > 0)
                {
                    LogListBox?.ScrollIntoView(_filteredEntries[^1]);
                }
            }
        }
        catch
        {
            // UI-Fehler ignorieren
        }
    }

    private void RefreshEntries()
    {
        if (_isClosing || !_isInitialized)
        {
            return;
        }

        try
        {
            _filteredEntries.Clear();

            foreach (var entry in _logger.GetEntries())
            {
                if (ShouldShowEntry(entry))
                {
                    _filteredEntries.Add(entry);
                }
            }

            UpdateEntryCount();

            if (AutoScrollCheckBox?.IsChecked == true && _filteredEntries.Count > 0)
            {
                LogListBox?.ScrollIntoView(_filteredEntries[^1]);
            }
        }
        catch
        {
            // Fehler ignorieren
        }
    }

    private bool ShouldShowEntry(LogEntry entry)
    {
        // Null-Checks für alle CheckBoxen
        if (!_isInitialized)
        {
            return true;
        }

        return entry.Level switch
        {
            LogLevel.Debug => ShowDebugCheckBox?.IsChecked == true,
            LogLevel.Info => ShowInfoCheckBox?.IsChecked == true,
            LogLevel.Warning => ShowWarningCheckBox?.IsChecked == true,
            LogLevel.Error => ShowErrorCheckBox?.IsChecked == true,
            _ => true
        };
    }

    private void UpdateEntryCount()
    {
        if (!_isInitialized || _isClosing || EntryCountText is null)
        {
            return;
        }

        try
        {
            var errorCount = _logger.ErrorCount;
            var totalShown = _filteredEntries.Count;

            EntryCountText.Text = errorCount > 0
                ? $"{totalShown} Eintraege ({errorCount} Fehler)"
                : $"{totalShown} Eintraege";
        }
        catch
        {
            // Ignorieren
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        // Wichtig: Nur wenn bereits initialisiert
        if (_isInitialized && !_isClosing)
        {
            RefreshEntries();
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Log wirklich leeren?",
            "Bestaetigung",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _logger.Clear();
            _filteredEntries.Clear();
            UpdateEntryCount();
        }
    }

    private void OpenLogFileButton_Click(object sender, RoutedEventArgs e)
    {
        var logPath = Path.Combine(Constants.AppDataFolder, Constants.LogFileName);

        if (File.Exists(logPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo(logPath)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Log-Datei konnte nicht geoeffnet werden:\n{logPath}\n\nFehler: {ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        else
        {
            MessageBox.Show(
                this,
                "Log-Datei existiert noch nicht.",
                "Information",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}