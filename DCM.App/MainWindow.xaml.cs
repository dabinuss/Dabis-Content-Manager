using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Core.Logging;
using DCM.Core.Models;
using DCM.Core.Services;
using DCM.Llm;
using DCM.YouTube;
using DCM.Transcription;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace DCM.App;

public partial class MainWindow : Window
{
    public string AppVersion { get; set; }
    private readonly TemplateService _templateService;
    private readonly ITemplateRepository _templateRepository;
    private readonly ISettingsProvider _settingsProvider;
    private readonly YouTubePlatformClient _youTubeClient;
    private readonly UploadHistoryService _uploadHistoryService;
    private readonly SimpleFallbackSuggestionService _fallbackSuggestionService;
    private readonly IAppLogger _logger;

    private ILlmClient _llmClient;
    private IContentSuggestionService _contentSuggestionService;
    private readonly UploadService _uploadService;
    private AppSettings _settings = new();

    private readonly List<Template> _loadedTemplates = new();
    private Template? _currentEditingTemplate;

    private readonly List<YouTubePlaylistInfo> _youTubePlaylists = new();

    private List<UploadHistoryEntry> _allHistoryEntries = new();

    private CancellationTokenSource? _currentLlmCts;

    private LogWindow? _logWindow;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();

        AppVersion = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.0"}";

        _logger = AppLogger.Instance;
        _logger.Info("Anwendung gestartet", "MainWindow");

        // Fensterzustand wiederherstellen (smart, ohne das Fenster zu "verlieren")
        WindowStateManager.Apply(this);

        _templateService = new TemplateService(_logger);
        _settingsProvider = new JsonSettingsProvider(_logger);
        _templateRepository = new JsonTemplateRepository(_logger);
        _youTubeClient = new YouTubePlatformClient(_logger);
        _uploadHistoryService = new UploadHistoryService(null, _logger);
        _fallbackSuggestionService = new SimpleFallbackSuggestionService(_logger);

        LoadSettings();

        _llmClient = CreateLlmClient(_settings.Llm);
        _contentSuggestionService = new ContentSuggestionService(
            _llmClient,
            _fallbackSuggestionService,
            _settings.Llm,
            _logger);

        _uploadService = new UploadService(new IPlatformClient[] { _youTubeClient }, _templateService, _logger);

        InitializePlatformComboBox();
        InitializeLanguageComboBox();
        InitializeChannelLanguageComboBox();
        InitializeSchedulingDefaults();
        InitializeLlmSettingsTab();
        LoadTemplates();
        InitializeTranscriptionService();
        UpdateYouTubeStatusText();
        LoadUploadHistory();

        LlmTemperatureSlider.ValueChanged += LlmTemperatureSlider_ValueChanged;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    #region Lifecycle

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;

        // Fensterzustand speichern
        WindowStateManager.Save(this);

        // Event-Handler zuerst entfernen
        try
        {
            _logger.EntryAdded -= OnLogEntryAdded;
        }
        catch
        {
            // Ignorieren
        }

        _logger.Info("Anwendung wird beendet", "MainWindow");

        // LogWindow schließen falls offen
        CloseLogWindowSafely();

        // LLM-Operationen abbrechen
        CancelCurrentLlmOperation();

        // Transkription abbrechen und aufräumen
        DisposeTranscriptionService();

        // LLM-Client aufräumen
        if (_llmClient is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Ignorieren
            }
        }
    }

    private void CloseLogWindowSafely()
    {
        var logWindow = _logWindow;
        _logWindow = null;

        if (logWindow is null)
        {
            return;
        }

        try
        {
            if (logWindow.IsLoaded)
            {
                logWindow.Close();
            }
        }
        catch
        {
            // Ignorieren - Fenster könnte bereits geschlossen sein
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Event-Handler für Log-Updates registrieren (nach UI-Initialisierung)
        _logger.EntryAdded += OnLogEntryAdded;

        if (_settings.AutoConnectYouTube)
        {
            await TryAutoConnectYouTubeAsync();
        }

        UpdateLlmStatusText();
        UpdateLogLinkIndicator();
    }

    private void OnLogEntryAdded(LogEntry entry)
    {
        if (_isClosing)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_isClosing)
                    {
                        UpdateLogLinkIndicatorSafe();
                    }
                });
            }
            catch
            {
                // Dispatcher könnte bereits heruntergefahren sein
            }
            return;
        }

        UpdateLogLinkIndicatorSafe();
    }

    private void UpdateLogLinkIndicatorSafe()
    {
        if (_isClosing || LogLinkTextBlock is null)
        {
            return;
        }

        try
        {
            UpdateLogLinkIndicator();
        }
        catch
        {
            // UI-Fehler ignorieren
        }
    }

    #endregion

    #region Language Selection

    private bool _isLanguageInitializing;

    private void InitializeLanguageComboBox()
    {
        _isLanguageInitializing = true;

        LanguageComboBox.ItemsSource = LocalizationManager.Instance.AvailableLanguages;

        // Aktuelle Sprache auswählen
        var currentLang = _settings.Language ?? LocalizationManager.Instance.CurrentLanguage;
        foreach (LanguageInfo lang in LanguageComboBox.Items)
        {
            if (lang.Code == currentLang)
            {
                LanguageComboBox.SelectedItem = lang;
                break;
            }
        }

        _isLanguageInitializing = false;
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLanguageInitializing)
        {
            return;
        }

        if (LanguageComboBox.SelectedItem is not LanguageInfo selectedLang)
        {
            return;
        }

        // Sprache wechseln
        LocalizationManager.Instance.SetLanguage(selectedLang.Code);

        // In Settings speichern
        _settings.Language = selectedLang.Code;
        SaveSettings();

        StatusTextBlock.Text = $"Sprache geändert: {selectedLang.DisplayName}";
        _logger.Info($"Sprache geändert auf: {selectedLang.Code}", "Settings");
    }

    #endregion

    #region LLM Client Management

    private void CancelCurrentLlmOperation()
    {
        try
        {
            _currentLlmCts?.Cancel();
            _currentLlmCts?.Dispose();
        }
        catch
        {
            // Ignorieren
        }
        finally
        {
            _currentLlmCts = null;
        }
    }

    private CancellationToken GetNewLlmCancellationToken()
    {
        CancelCurrentLlmOperation();
        _currentLlmCts = new CancellationTokenSource();
        return _currentLlmCts.Token;
    }

    private ILlmClient CreateLlmClient(LlmSettings settings)
    {
        if (settings.IsLocalMode && !string.IsNullOrWhiteSpace(settings.LocalModelPath))
        {
            return new LocalLlmClient(settings, _logger);
        }

        return new NullLlmClient();
    }

    private void RecreateLlmClient()
    {
        CancelCurrentLlmOperation();

        if (_llmClient is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Ignorieren
            }
        }

        _llmClient = CreateLlmClient(_settings.Llm);
        _contentSuggestionService = new ContentSuggestionService(
            _llmClient,
            _fallbackSuggestionService,
            _settings.Llm,
            _logger);

        UpdateLlmStatusText();
    }

    #endregion

    #region Initialization

    private void InitializePlatformComboBox()
    {
        // PlatformComboBox wurde durch Checkboxen ersetzt
        // Nur noch TemplatePlatformComboBox initialisieren
        TemplatePlatformComboBox.ItemsSource = Enum.GetValues(typeof(PlatformType));

        SelectComboBoxItemByTag(VisibilityComboBox, _settings.DefaultVisibility);
    }

    private void InitializeChannelLanguageComboBox()
    {
        var cultures = CultureInfo
            .GetCultures(CultureTypes.NeutralCultures)
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .OrderBy(c => c.DisplayName)
            .Select(c => $"{c.DisplayName} ({c.Name})")
            .ToList();

        ChannelPersonaLanguageTextBox.ItemsSource = cultures;
    }

    private void InitializeSchedulingDefaults()
    {
        TimeSpan defaultTime;

        if (!string.IsNullOrWhiteSpace(_settings.DefaultSchedulingTime) &&
            TimeSpan.TryParse(_settings.DefaultSchedulingTime, CultureInfo.CurrentCulture, out var parsed))
        {
            defaultTime = parsed;
        }
        else
        {
            defaultTime = TimeSpan.Parse(Constants.DefaultSchedulingTime);
        }

        ScheduleDatePicker.SelectedDate = DateTime.Today.AddDays(1);
        ScheduleTimeTextBox.Text = defaultTime.ToString(@"hh\:mm");
        UpdateScheduleControlsEnabled();
    }

    private void InitializeLlmSettingsTab()
    {
        ApplyLlmSettingsToUi();
    }

    #endregion

    #region Video Drop Zone

    private readonly string[] _allowedVideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" };

    private void VideoDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length == 1)
            {
                var ext = System.IO.Path.GetExtension(files[0]).ToLowerInvariant();
                if (_allowedVideoExtensions.Contains(ext))
                {
                    e.Effects = DragDropEffects.Copy;
                    VideoDropZone.BorderBrush = (SolidColorBrush)FindResource("PrimaryBrush");
                }
            }
        }
        e.Handled = true;
    }

    private void VideoDrop_DragLeave(object sender, DragEventArgs e)
    {
        VideoDropZone.BorderBrush = (SolidColorBrush)FindResource("BorderBrush");
    }

    private void VideoDrop_Drop(object sender, DragEventArgs e)
    {
        VideoDropZone.BorderBrush = (SolidColorBrush)FindResource("BorderBrush");

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length == 1)
            {
                var ext = System.IO.Path.GetExtension(files[0]).ToLowerInvariant();
                if (_allowedVideoExtensions.Contains(ext))
                {
                    SetVideoFile(files[0]);
                }
            }
        }
    }

    private void VideoDropZone_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Video-Dateien|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm|Alle Dateien|*.*",
            Title = "Video auswählen"
        };

        if (!string.IsNullOrEmpty(_settings?.DefaultVideoFolder) &&
            System.IO.Directory.Exists(_settings.DefaultVideoFolder))
        {
            dialog.InitialDirectory = _settings.DefaultVideoFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            SetVideoFile(dialog.FileName);
        }
    }

    private void SetVideoFile(string filePath)
    {

        VideoPathTextBox.Text = filePath;
        var fileInfo = new System.IO.FileInfo(filePath);

        VideoDropEmptyState.Visibility = Visibility.Collapsed;
        VideoDropSelectedState.Visibility = Visibility.Visible;
        UpdateUploadButtonState();
        UpdateTranscriptionButtonState();

        _logger.Info($"Video ausgewählt: {fileInfo.Name}", "Upload");  // ✅ Ist da!

        // Auto-Transkription wenn aktiviert
        _ = LoadVideoFileInfoAsync(filePath);
    }

    private async Task LoadVideoFileInfoAsync(string filePath)
    {
        try
        {
            // File-Info im Hintergrund laden (nicht UI-Thread blockieren)
            var (fileName, fileSize) = await Task.Run(() =>
            {
                var fileInfo = new FileInfo(filePath);
                return (fileInfo.Name, fileInfo.Length);
            });

            // UI-Updates zurück auf UI-Thread
            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    VideoFileNameTextBlock.Text = fileName;
                    VideoFileSizeTextBlock.Text = FormatFileSize(fileSize);
                });
            }
            else
            {
                VideoFileNameTextBlock.Text = fileName;
                VideoFileSizeTextBlock.Text = FormatFileSize(fileSize);
            }

            _logger.Info($"Video ausgewählt: {fileName}", "Upload");

            // Auto-Transkription starten (falls aktiviert)
            // Dies läuft jetzt komplett unabhängig und blockiert nicht
            _ = TryAutoTranscribeAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Laden der Video-Informationen: {ex.Message}", "Upload", ex);

            // Fallback: Zeige zumindest den Dateinamen
            await Dispatcher.InvokeAsync(() =>
            {
                VideoFileNameTextBlock.Text = Path.GetFileName(filePath);
                VideoFileSizeTextBlock.Text = "? MB";
            });
        }
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateUploadButtonState();
    }

    private void FocusTargetOnContainerClick(object sender, MouseButtonEventArgs e)
    {
        // Klicks auf Buttons oder Textboxen ignorieren (damit sie normal funktionieren)
        if (e.OriginalSource is DependencyObject d)
        {
            while (d != null)
            {
                // Text-Eingaben: normal verhalten (Cursor setzen, markieren etc.)
                if (d is TextBoxBase || d is PasswordBox)
                {
                    return;
                }

                // Buttons weiterhin komplett ignorieren
                if (d is ButtonBase)
                {
                    return;
                }

                d = VisualTreeHelper.GetParent(d);
            }
        }

        // Nur wenn auf den Container selbst / "leeren" Bereich geklickt wird:
        if (sender is FrameworkElement fe && fe.Tag is Control target && target.Focusable)
        {
            target.Focus();

            // Bei TextBox Cursor ans Ende setzen (nice to have)
            if (target is TextBox tb)
            {
                tb.CaretIndex = tb.Text?.Length ?? 0;
            }

            // Event nicht weiterreichen (wir wollten ja nur Fokus setzen)
            e.Handled = true;
        }
    }


    private void UpdateUploadButtonState()
    {
        bool hasVideo = !string.IsNullOrWhiteSpace(VideoPathTextBox.Text);
        bool hasTitle = !string.IsNullOrWhiteSpace(TitleTextBox.Text);

        UploadButton.IsEnabled = hasVideo && hasTitle;
    }

    #endregion

    #region Thumbnail Drop Zone

    private readonly string[] _allowedImageExtensions = { ".png", ".jpg", ".jpeg" };

    private void ThumbnailDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length == 1)
            {
                var ext = System.IO.Path.GetExtension(files[0]).ToLowerInvariant();
                if (_allowedImageExtensions.Contains(ext))
                {
                    e.Effects = DragDropEffects.Copy;
                    ThumbnailDropZone.BorderBrush = (SolidColorBrush)FindResource("PrimaryBrush");
                }
            }
        }
        e.Handled = true;
    }

    private void ThumbnailDrop_DragLeave(object sender, DragEventArgs e)
    {
        ThumbnailDropZone.BorderBrush = (SolidColorBrush)FindResource("BorderBrush");
    }

    private void ThumbnailDrop_Drop(object sender, DragEventArgs e)
    {
        ThumbnailDropZone.BorderBrush = (SolidColorBrush)FindResource("BorderBrush");

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length == 1)
            {
                var ext = System.IO.Path.GetExtension(files[0]).ToLowerInvariant();
                if (_allowedImageExtensions.Contains(ext))
                {
                    SetThumbnailFile(files[0]);
                }
            }
        }
    }

    private void ThumbnailDropZone_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Bilder|*.png;*.jpg;*.jpeg|Alle Dateien|*.*",
            Title = "Thumbnail auswählen"
        };

        if (!string.IsNullOrEmpty(_settings?.DefaultThumbnailFolder) &&
            System.IO.Directory.Exists(_settings.DefaultThumbnailFolder))
        {
            dialog.InitialDirectory = _settings.DefaultThumbnailFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            SetThumbnailFile(dialog.FileName);
        }
    }

    private void SetThumbnailFile(string filePath)
    {
        ThumbnailPathTextBox.Text = filePath;

        var fileInfo = new System.IO.FileInfo(filePath);
        ThumbnailFileNameTextBlock.Text = fileInfo.Name;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            ThumbnailPreviewImage.Source = bitmap;

            ThumbnailEmptyState.Visibility = Visibility.Collapsed;
            ThumbnailPreviewState.Visibility = Visibility.Visible;

            _logger.Info($"Thumbnail ausgewählt: {fileInfo.Name}", "Upload");
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim Laden des Thumbnails: {ex.Message}", "Upload", ex);
        }
    }

    private void ThumbnailClearButton_Click(object sender, RoutedEventArgs e)
    {
        ThumbnailPathTextBox.Text = string.Empty;
        ThumbnailPreviewImage.Source = null;

        ThumbnailEmptyState.Visibility = Visibility.Visible;
        ThumbnailPreviewState.Visibility = Visibility.Collapsed;
    }

    private void VideoChangeButton_Click(object sender, RoutedEventArgs e)
    {
        // Delegiere an die Auswahl-Logik
        VideoDropZone_Click(sender, null!);
    }

    #endregion

    #region Upload Events

    private void ApplyTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        Template? tmpl = TemplateComboBox.SelectedItem as Template;

        if (tmpl is null && TemplateListBox.SelectedItem is Template tabTemplate)
        {
            tmpl = tabTemplate;
        }

        if (tmpl is null)
        {
            StatusTextBlock.Text = "Kein Template ausgewählt.";
            return;
        }

        var project = BuildUploadProjectFromUi(includeScheduling: false);
        var result = _templateService.ApplyTemplate(tmpl.Body, project);

        DescriptionTextBox.Text = result;
        StatusTextBlock.Text = $"Template \"{tmpl.Name}\" angewendet.";

        _logger.Info($"Template angewendet: {tmpl.Name}", "Template");
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.ConfirmBeforeUpload)
        {
            var confirmResult = MessageBox.Show(
                this,
                "Upload wirklich starten?",
                "Bestätigung",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes)
            {
                StatusTextBlock.Text = "Upload abgebrochen.";
                return;
            }
        }

        var project = BuildUploadProjectFromUi(includeScheduling: true);

        if (!string.IsNullOrWhiteSpace(project.ThumbnailPath) &&
            !File.Exists(project.ThumbnailPath))
        {
            StatusTextBlock.Text = "Hinweis: Thumbnail-Datei wurde nicht gefunden. Upload wird ohne Thumbnail fortgesetzt.";
            _logger.Warning($"Thumbnail nicht gefunden: {project.ThumbnailPath}", "Upload");
            project.ThumbnailPath = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(project.ThumbnailPath))
        {
            var fileInfo = new FileInfo(project.ThumbnailPath);
            if (fileInfo.Length > Constants.MaxThumbnailSizeBytes)
            {
                StatusTextBlock.Text = "Thumbnail ist größer als 2 MB und wird nicht verwendet.";
                _logger.Warning($"Thumbnail zu groß: {fileInfo.Length / 1024 / 1024:F2} MB", "Upload");
                project.ThumbnailPath = string.Empty;
            }
        }

        try
        {
            project.Validate();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehlerhafte Eingaben: {ex.Message}";
            _logger.Warning($"Validierungsfehler: {ex.Message}", "Upload");
            return;
        }

        if (project.Platform == PlatformType.YouTube && !_youTubeClient.IsConnected)
        {
            StatusTextBlock.Text = "Bitte zuerst im Tab \"Konten\" mit YouTube verbinden.";
            return;
        }

        _logger.Info($"Upload gestartet: {project.Title}", "Upload");
        StatusTextBlock.Text = "Upload wird vorbereitet...";
        ShowUploadProgress("Upload wird vorbereitet...");

        var progressReporter = new Progress<UploadProgressInfo>(ReportUploadProgress);

        try
        {
            Template? selectedTemplate = TemplateComboBox.SelectedItem as Template;

            var result = await _uploadService.UploadAsync(
                project,
                selectedTemplate,
                progressReporter,
                CancellationToken.None);

            _uploadHistoryService.AddEntry(project, result);
            LoadUploadHistory();

            if (result.Success)
            {
                StatusTextBlock.Text = $"Upload erfolgreich: {result.VideoUrl}";
                _logger.Info($"Upload erfolgreich: {result.VideoUrl}", "Upload");

                if (_settings.OpenBrowserAfterUpload && result.VideoUrl is not null)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(result.VideoUrl.ToString())
                        {
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // Browser öffnen ist Komfort – Fehler nicht kritisch.
                    }
                }
            }
            else
            {
                StatusTextBlock.Text = $"Upload fehlgeschlagen: {result.ErrorMessage}";
                _logger.Error($"Upload fehlgeschlagen: {result.ErrorMessage}", "Upload");
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Unerwarteter Fehler beim Upload: {ex.Message}";
            _logger.Error($"Unerwarteter Fehler beim Upload: {ex.Message}", "Upload", ex);
        }
        finally
        {
            HideUploadProgress();
            UpdateLogLinkIndicator();
        }
    }

    #endregion

    #region Content Generation Events

    private async void GenerateTitleButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();

        StatusTextBlock.Text = "Generiere Titelvorschläge...";
        GenerateTitleButton.IsEnabled = false;

        var cancellationToken = GetNewLlmCancellationToken();

        _logger.Debug("Titelgenerierung gestartet", "LLM");

        try
        {
            var titles = await _contentSuggestionService.SuggestTitlesAsync(
                project,
                _settings.Persona,
                cancellationToken);

            if (titles is null || titles.Count == 0)
            {
                TitleTextBox.Text = "[Keine Vorschläge]";
                StatusTextBlock.Text = "Keine Titelvorschläge erhalten.";
                _logger.Warning("Keine Titelvorschläge erhalten", "LLM");
                return;
            }

            TitleTextBox.Text = titles[0];
            StatusTextBlock.Text = $"Titelvorschlag eingefügt. ({titles.Count} Vorschläge)";
            _logger.Info($"Titelvorschlag generiert: {titles[0]}", "LLM");
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Titelgenerierung abgebrochen.";
            _logger.Debug("Titelgenerierung abgebrochen", "LLM");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler bei Titelgenerierung: {ex.Message}";
            _logger.Error($"Fehler bei Titelgenerierung: {ex.Message}", "LLM", ex);
        }
        finally
        {
            GenerateTitleButton.IsEnabled = true;
            UpdateLogLinkIndicator();
        }
    }

    private async void GenerateDescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();

        StatusTextBlock.Text = "Generiere Beschreibungsvorschlag...";
        GenerateDescriptionButton.IsEnabled = false;

        var cancellationToken = GetNewLlmCancellationToken();

        _logger.Debug("Beschreibungsgenerierung gestartet", "LLM");

        try
        {
            var description = await _contentSuggestionService.SuggestDescriptionAsync(
                project,
                _settings.Persona,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(description))
            {
                DescriptionTextBox.Text = description;
                StatusTextBlock.Text = "Beschreibungsvorschlag eingefügt.";
                _logger.Info("Beschreibungsvorschlag generiert", "LLM");
            }
            else
            {
                StatusTextBlock.Text = "Keine Beschreibungsvorschläge verfügbar.";
                _logger.Warning("Keine Beschreibungsvorschläge verfügbar", "LLM");
            }
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Beschreibungsgenerierung abgebrochen.";
            _logger.Debug("Beschreibungsgenerierung abgebrochen", "LLM");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler bei Beschreibungsgenerierung: {ex.Message}";
            _logger.Error($"Fehler bei Beschreibungsgenerierung: {ex.Message}", "LLM", ex);
        }
        finally
        {
            GenerateDescriptionButton.IsEnabled = true;
            UpdateLogLinkIndicator();
        }
    }

    private async void GenerateTagsButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();

        StatusTextBlock.Text = "Generiere Tag-Vorschläge...";
        GenerateTagsButton.IsEnabled = false;

        var cancellationToken = GetNewLlmCancellationToken();

        _logger.Debug("Tag-Generierung gestartet", "LLM");

        try
        {
            var tags = await _contentSuggestionService.SuggestTagsAsync(
                project,
                _settings.Persona,
                cancellationToken);

            if (tags is not null && tags.Count > 0)
            {
                TagsTextBox.Text = string.Join(", ", tags);
                StatusTextBlock.Text = $"Tag-Vorschläge eingefügt. ({tags.Count} Tags)";
                _logger.Info($"Tag-Vorschläge generiert: {tags.Count} Tags", "LLM");
            }
            else
            {
                StatusTextBlock.Text = "Keine Tag-Vorschläge verfügbar.";
                _logger.Warning("Keine Tag-Vorschläge verfügbar", "LLM");
            }
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Tag-Generierung abgebrochen.";
            _logger.Debug("Tag-Generierung abgebrochen", "LLM");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler bei Tag-Generierung: {ex.Message}";
            _logger.Error($"Fehler bei Tag-Generierung: {ex.Message}", "LLM", ex);
        }
        finally
        {
            GenerateTagsButton.IsEnabled = true;
            UpdateLogLinkIndicator();
        }
    }

    #endregion

    #region Upload Progress UI

    private void ShowUploadProgress(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowUploadProgress(message));
            return;
        }

        UploadProgressBar.Visibility = Visibility.Visible;
        UploadProgressLabel.Visibility = Visibility.Visible;

        UploadProgressBar.IsIndeterminate = true;
        UploadProgressBar.Value = 0;
        UploadProgressLabel.Text = message;
    }

    private void ReportUploadProgress(UploadProgressInfo info)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ReportUploadProgress(info));
            return;
        }

        UploadProgressBar.Visibility = Visibility.Visible;
        UploadProgressLabel.Visibility = Visibility.Visible;

        UploadProgressBar.IsIndeterminate = info.IsIndeterminate;

        if (!info.IsIndeterminate)
        {
            var percent = double.IsNaN(info.Percent) ? 0 : Math.Clamp(info.Percent, 0, 100);
            UploadProgressBar.Value = percent;
        }

        if (!string.IsNullOrWhiteSpace(info.Message))
        {
            UploadProgressLabel.Text = info.Message;
            StatusTextBlock.Text = info.Message;
        }
    }

    private void HideUploadProgress()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(HideUploadProgress);
            return;
        }

        UploadProgressBar.Visibility = Visibility.Collapsed;
        UploadProgressLabel.Visibility = Visibility.Collapsed;
        UploadProgressBar.IsIndeterminate = false;
        UploadProgressBar.Value = 0;
        UploadProgressLabel.Text = string.Empty;
    }

    #endregion

    #region History

    private void LoadUploadHistory()
    {
        try
        {
            _allHistoryEntries = _uploadHistoryService
                .GetAll()
                .OrderByDescending(e => e.DateTime)
                .ToList();

            ApplyHistoryFilter();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Upload-Historie konnte nicht geladen werden: {ex.Message}";
            _logger.Error($"Upload-Historie konnte nicht geladen werden: {ex.Message}", "History", ex);
        }
    }

    private void ApplyHistoryFilter()
    {
        if (HistoryDataGrid is null)
        {
            return;
        }

        IEnumerable<UploadHistoryEntry> filtered = _allHistoryEntries;

        if (HistoryPlatformFilterComboBox?.SelectedItem is ComboBoxItem pItem &&
            pItem.Tag is PlatformType platformFilter)
        {
            filtered = filtered.Where(e => e.Platform == platformFilter);
        }

        if (HistoryStatusFilterComboBox?.SelectedItem is ComboBoxItem sItem &&
            sItem.Tag is string statusTag)
        {
            if (statusTag == "Success")
            {
                filtered = filtered.Where(e => e.Success);
            }
            else if (statusTag == "Error")
            {
                filtered = filtered.Where(e => !e.Success);
            }
        }

        HistoryDataGrid.ItemsSource = filtered.ToList();
    }

    private void HistoryFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplyHistoryFilter();
    }

    private void HistoryClearButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "Die komplette Upload-Historie wirklich löschen?",
            "Bestätigung",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _uploadHistoryService.Clear();
            _allHistoryEntries.Clear();
            ApplyHistoryFilter();
            StatusTextBlock.Text = "Upload-Historie gelöscht.";
            _logger.Info("Upload-Historie gelöscht", "History");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Historie konnte nicht gelöscht werden: {ex.Message}";
            _logger.Error($"Historie konnte nicht gelöscht werden: {ex.Message}", "History", ex);
        }
    }

    private void HistoryDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryDataGrid.SelectedItem is UploadHistoryEntry entry &&
            entry.VideoUrl is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(entry.VideoUrl.ToString())
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // Fehler ignorieren
            }
        }
    }

    private void OpenUrlInBrowser(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.ToString())
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Fehler ignorieren
        }

        e.Handled = true;
    }

    #endregion

    #region Log Window

    private void LogLink_Click(object sender, MouseButtonEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        try
        {
            if (_logWindow is null || !_logWindow.IsLoaded)
            {
                _logWindow = new LogWindow();
                _logWindow.Owner = this;
                _logWindow.Closed += OnLogWindowClosed;
                _logWindow.Show();
            }
            else
            {
                _logWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"LogWindow konnte nicht geöffnet werden: {ex.Message}", "MainWindow", ex);
            MessageBox.Show(
                this,
                $"Log-Fenster konnte nicht geöffnet werden:\n{ex.Message}",
                "Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnLogWindowClosed(object? sender, EventArgs e)
    {
        if (sender is LogWindow lw)
        {
            lw.Closed -= OnLogWindowClosed;
        }

        _logWindow = null;
    }

    private void UpdateLogLinkIndicator()
    {
        if (_isClosing || LogLinkTextBlock is null)
        {
            return;
        }

        try
        {
            if (_logger.HasErrors)
            {
                LogLinkTextBlock.Text = $"📋 Log ({_logger.ErrorCount} ⚠)";
                LogLinkTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0x4E, 0x45));
            }
            else
            {
                LogLinkTextBlock.Text = "📋 Log";
                LogLinkTextBlock.ClearValue(ForegroundProperty);
            }
        }
        catch
        {
            // UI-Update-Fehler ignorieren
        }
    }

    #endregion

    #region Updates

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            StatusTextBlock.Text = "Prüfe auf Updates...";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DabisContentManager/1.0");

            using var response = await httpClient.GetAsync("https://api.github.com/repos/dabinuss/Dabis-Content-Manager/releases/latest");
            if (!response.IsSuccessStatusCode)
            {
                StatusTextBlock.Text = "Update-Prüfung nicht möglich.";
                _logger.Warning($"Update-Prüfung fehlgeschlagen: HTTP {(int)response.StatusCode}", "Updates");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var htmlUrl = root.TryGetProperty("html_url", out var urlProp)
                ? urlProp.GetString()
                : "https://github.com/dabinuss/Dabis-Content-Manager/releases";

            if (string.IsNullOrWhiteSpace(tagName))
            {
                StatusTextBlock.Text = "Update-Informationen konnten nicht gelesen werden.";
                _logger.Warning("Update-Informationen ohne tag_name erhalten", "Updates");
                return;
            }

            var latestVersionString = tagName.TrimStart('v', 'V');
            if (!Version.TryParse(latestVersionString, out var latestVersion))
            {
                StatusTextBlock.Text = $"Versionsformat nicht erkannt: {tagName}";
                _logger.Warning($"Konnte Versionsnummer aus Tag '{tagName}' nicht parsen.", "Updates");
                return;
            }

            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
                                ?? new Version(0, 0);

            if (latestVersion <= currentVersion)
            {
                StatusTextBlock.Text = $"Du verwendest die aktuelle Version ({AppVersion}).";
                _logger.Info($"Keine neuere Version gefunden. Aktuell: {currentVersion}, Remote: {latestVersion}", "Updates");
                return;
            }

            StatusTextBlock.Text = $"Neue Version verfügbar: v{latestVersion}";
            _logger.Info($"Neue Version gefunden. Aktuell: {currentVersion}, Remote: {latestVersion}", "Updates");

            var result = MessageBox.Show(
                this,
                $"Es ist eine neue Version verfügbar:\n\nAktuell: v{currentVersion}\nNeu: v{latestVersion}\n\nGitHub-Releases öffnen?",
                "Update verfügbar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(htmlUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(htmlUrl)
                    {
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Kein Drama
                }
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Update-Prüfung fehlgeschlagen: {ex.Message}";
            _logger.Error($"Update-Prüfung fehlgeschlagen: {ex.Message}", "Updates", ex);
        }
        finally
        {
            UpdateLogLinkIndicator();
        }
    }

    private async void CheckUpdatesHyperlink_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync();
    }

    #endregion

    #region Transkript-Export

    private void TranscriptionExportButton_Click(object sender, RoutedEventArgs e)
    {
        var transcript = TranscriptTextBox.Text;
        if (string.IsNullOrWhiteSpace(transcript))
        {
            MessageBox.Show(
                this,
                "Es ist kein Transkript vorhanden, das exportiert werden kann.",
                "Kein Transkript",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        string baseName;
        if (!string.IsNullOrWhiteSpace(VideoPathTextBox.Text))
        {
            baseName = Path.GetFileNameWithoutExtension(VideoPathTextBox.Text);
        }
        else
        {
            baseName = "transcript";
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Transkript exportieren",
            FileName = $"{baseName}_transcript.txt",
            Filter = "Textdatei|*.txt|Alle Dateien|*.*"
        };

        if (!string.IsNullOrWhiteSpace(_settings?.DefaultVideoFolder) &&
            Directory.Exists(_settings.DefaultVideoFolder))
        {
            dialog.InitialDirectory = _settings.DefaultVideoFolder;
        }

        var result = dialog.ShowDialog(this);
        if (result != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, transcript, System.Text.Encoding.UTF8);
            StatusTextBlock.Text = $"Transkript exportiert: {dialog.FileName}";
            _logger.Info($"Transkript exportiert: {dialog.FileName}", "Transcription");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Transkript konnte nicht exportiert werden: {ex.Message}";
            _logger.Error($"Transkript konnte nicht exportiert werden: {ex.Message}", "Transcription", ex);

            MessageBox.Show(
                this,
                $"Transkript konnte nicht exportiert werden:\n{ex.Message}",
                "Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    #endregion

    #region Helpers

    private UploadProject BuildUploadProjectFromUi(bool includeScheduling)
    {
        // Platform aus Checkboxen ermitteln
        var platform = PlatformType.YouTube; // Standard
        if (PlatformYouTubeToggle.IsChecked == true)
        {
            platform = PlatformType.YouTube;
        }
        // Später können hier weitere Plattformen hinzugefügt werden

        var visibility = _settings.DefaultVisibility;
        if (VisibilityComboBox.SelectedItem is ComboBoxItem visItem && visItem.Tag is VideoVisibility visEnum)
        {
            visibility = visEnum;
        }

        string? playlistId = null;
        if (PlaylistComboBox.SelectedItem is YouTubePlaylistInfo plItem)
        {
            playlistId = plItem.Id;
        }
        else
        {
            playlistId = _settings.DefaultPlaylistId;
        }

        DateTimeOffset? scheduledTime = null;
        if (includeScheduling && ScheduleCheckBox.IsChecked == true)
        {
            if (ScheduleDatePicker.SelectedDate is DateTime date)
            {
                var timeText = ScheduleTimeTextBox.Text;
                TimeSpan timeOfDay;

                if (!string.IsNullOrWhiteSpace(timeText) &&
                    TimeSpan.TryParse(timeText, CultureInfo.CurrentCulture, out timeOfDay))
                {
                    // ok
                }
                else
                {
                    timeOfDay = TimeSpan.Parse(Constants.DefaultSchedulingTime);
                }

                var localDateTime = date.Date + timeOfDay;
                scheduledTime = new DateTimeOffset(localDateTime, DateTimeOffset.Now.Offset);
            }
        }

        var project = new UploadProject
        {
            VideoFilePath = VideoPathTextBox.Text ?? string.Empty,
            Title = TitleTextBox.Text ?? string.Empty,
            Description = DescriptionTextBox.Text ?? string.Empty,
            Platform = platform,
            Visibility = visibility,
            PlaylistId = playlistId,
            ScheduledTime = scheduledTime,
            ThumbnailPath = ThumbnailPathTextBox.Text,
            TranscriptText = TranscriptTextBox.Text
        };

        project.SetTagsFromCsv(TagsTextBox.Text ?? string.Empty);

        return project;
    }

    private void UpdateScheduleControlsEnabled()
    {
        var enabled = ScheduleCheckBox.IsChecked == true;
        ScheduleDatePicker.IsEnabled = enabled;
        ScheduleTimeTextBox.IsEnabled = enabled;
    }

    #endregion

    #region Sidebar Navigation

    private void NavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radioButton)
        {
            return;
        }

        if (radioButton.Tag is not string tagString || !int.TryParse(tagString, out var pageIndex))
        {
            return;
        }

        ShowPage(pageIndex);
    }

    private void ShowPage(int pageIndex)
    {
        // Alle Pages verstecken
        if (PageUpload is not null) PageUpload.Visibility = Visibility.Collapsed;
        if (PageAccounts is not null) PageAccounts.Visibility = Visibility.Collapsed;
        if (PageChannel is not null) PageChannel.Visibility = Visibility.Collapsed;
        if (PageTemplates is not null) PageTemplates.Visibility = Visibility.Collapsed;
        if (PageHistory is not null) PageHistory.Visibility = Visibility.Collapsed;
        if (PageSettings is not null) PageSettings.Visibility = Visibility.Collapsed;

        // Gewählte Page anzeigen
        switch (pageIndex)
        {
            case 0:
                if (PageUpload is not null) PageUpload.Visibility = Visibility.Visible;
                break;
            case 1:
                if (PageAccounts is not null) PageAccounts.Visibility = Visibility.Visible;
                break;
            case 2:
                if (PageChannel is not null) PageChannel.Visibility = Visibility.Visible;
                break;
            case 3:
                if (PageTemplates is not null) PageTemplates.Visibility = Visibility.Visible;
                break;
            case 4:
                if (PageHistory is not null) PageHistory.Visibility = Visibility.Visible;
                break;
            case 5:
                if (PageSettings is not null) PageSettings.Visibility = Visibility.Visible;
                break;
        }
    }

    private void YouTubeServiceIcon_Click(object sender, MouseButtonEventArgs e)
    {
        // Navigiere zum Konten-Tab
        NavAccounts.IsChecked = true;
        ShowPage(1);
    }

    #endregion
}
