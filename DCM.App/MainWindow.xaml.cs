using System;
using System.Collections.Generic;
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
using DCM.App.Events;
using DCM.App.Infrastructure;
using DCM.App.Views;
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
using System.Threading.Tasks;

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
    private UploadView UploadView => UploadPageView ?? throw new InvalidOperationException("Upload view is not ready.");
    private readonly UiEventAggregator _eventAggregator = UiEventAggregator.Instance;
    private readonly List<IDisposable> _eventSubscriptions = new();

    public MainWindow()
    {
        InitializeComponent();
        AttachUploadViewEvents();
        AttachAccountsViewEvents();
        AttachChannelViewEvents();
        AttachTemplatesViewEvents();
        AttachHistoryViewEvents();
        AttachSettingsViewEvents();
        RegisterEventSubscriptions();

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
        UpdateYouTubeStatusText();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void AttachUploadViewEvents()
    {
        if (UploadPageView is null)
        {
            return;
        }

        UploadPageView.VideoDropDragOver += VideoDrop_DragOver;
        UploadPageView.VideoDropDragLeave += VideoDrop_DragLeave;
        UploadPageView.VideoDropDrop += VideoDrop_Drop;
        UploadPageView.VideoDropZoneClicked += VideoDropZone_Click;

        UploadPageView.VideoChangeButtonClicked += VideoChangeButton_Click;
        UploadPageView.UploadButtonClicked += UploadButton_Click;
        UploadPageView.TitleTextBoxTextChanged += TitleTextBox_TextChanged;
        UploadPageView.GenerateTitleButtonClicked += GenerateTitleButton_Click;
        UploadPageView.GenerateDescriptionButtonClicked += GenerateDescriptionButton_Click;
        UploadPageView.TemplateComboBoxSelectionChanged += TemplateComboBox_SelectionChanged;
        UploadPageView.ApplyTemplateButtonClicked += ApplyTemplateButton_Click;
        UploadPageView.GenerateTagsButtonClicked += GenerateTagsButton_Click;
        UploadPageView.TranscribeButtonClicked += TranscribeButton_Click;
        UploadPageView.TranscriptionExportButtonClicked += TranscriptionExportButton_Click;

        UploadPageView.ThumbnailDropDragOver += ThumbnailDrop_DragOver;
        UploadPageView.ThumbnailDropDragLeave += ThumbnailDrop_DragLeave;
        UploadPageView.ThumbnailDropDrop += ThumbnailDrop_Drop;
        UploadPageView.ThumbnailDropZoneClicked += ThumbnailDropZone_Click;
        UploadPageView.ThumbnailClearButtonClicked += ThumbnailClearButton_Click;

        UploadPageView.FocusTargetOnContainerClicked += FocusTargetOnContainerClick;
    }

    private void AttachAccountsViewEvents()
    {
        if (AccountsPageView is null)
        {
            return;
        }

        AccountsPageView.AccountsServiceTabChecked += AccountsServiceTab_Checked;
        AccountsPageView.YouTubeConnectButtonClicked += YouTubeConnectButton_Click;
        AccountsPageView.YouTubeDisconnectButtonClicked += YouTubeDisconnectButton_Click;
        AccountsPageView.YouTubePlaylistsSelectionChanged += YouTubePlaylistsListBox_SelectionChanged;
    }

    private void AttachChannelViewEvents()
    {
        if (ChannelPageView is null)
        {
            return;
        }

        ChannelPageView.ChannelProfileSaveButtonClicked += ChannelProfileSaveButton_Click;
    }

    private void AttachTemplatesViewEvents()
    {
        if (TemplatesPageView is null)
        {
            return;
        }

        TemplatesPageView.TemplateNewButtonClicked += TemplateNewButton_Click;
        TemplatesPageView.TemplateEditButtonClicked += TemplateEditButton_Click;
        TemplatesPageView.TemplateDeleteButtonClicked += TemplateDeleteButton_Click;
        TemplatesPageView.TemplateSaveButtonClicked += TemplateSaveButton_Click;
        TemplatesPageView.TemplateListBoxSelectionChanged += TemplateListBox_SelectionChanged;
    }

    private void AttachHistoryViewEvents()
    {
        if (HistoryPageView is null)
        {
            return;
        }

        HistoryPageView.HistoryFilterChanged += (_, __) =>
            _eventAggregator.Publish(new HistoryFilterChangedEvent());

        HistoryPageView.HistoryClearButtonClicked += (_, __) =>
            _eventAggregator.Publish(new HistoryClearRequestedEvent());

        HistoryPageView.HistoryDataGridMouseDoubleClick += (_, __) =>
            _eventAggregator.Publish(new HistoryEntryOpenRequestedEvent(HistoryPageView.SelectedEntry));

        HistoryPageView.OpenUrlInBrowserRequested += (_, args) =>
        {
            _eventAggregator.Publish(new HistoryLinkOpenRequestedEvent(args.Uri));
            args.Handled = true;
        };
    }

    private void AttachSettingsViewEvents()
    {
        if (SettingsPageView is null)
        {
            return;
        }

        SettingsPageView.SettingsSaveButtonClicked += SettingsSaveButton_Click;
        SettingsPageView.DefaultVideoFolderBrowseButtonClicked += DefaultVideoFolderBrowseButton_Click;
        SettingsPageView.DefaultThumbnailFolderBrowseButtonClicked += DefaultThumbnailFolderBrowseButton_Click;
        SettingsPageView.LanguageComboBoxSelectionChanged += LanguageComboBox_SelectionChanged;
        SettingsPageView.TranscriptionDownloadButtonClicked += TranscriptionDownloadButton_Click;
        SettingsPageView.LlmModeComboBoxSelectionChanged += LlmModeComboBox_SelectionChanged;
        SettingsPageView.LlmModelPathBrowseButtonClicked += LlmModelPathBrowseButton_Click;
    }

    private void RegisterEventSubscriptions()
    {
        _eventSubscriptions.Add(_eventAggregator.Subscribe<HistoryFilterChangedEvent>(_ => OnHistoryFilterChanged()));
        _eventSubscriptions.Add(_eventAggregator.Subscribe<HistoryClearRequestedEvent>(_ => OnHistoryClearRequested()));
        _eventSubscriptions.Add(_eventAggregator.Subscribe<HistoryEntryOpenRequestedEvent>(e => OnHistoryEntryOpenRequested(e.Entry)));
        _eventSubscriptions.Add(_eventAggregator.Subscribe<HistoryLinkOpenRequestedEvent>(e => OnHistoryLinkOpenRequested(e.Uri)));
    }

    private void DisposeEventSubscriptions()
    {
        foreach (var subscription in _eventSubscriptions)
        {
            subscription.Dispose();
        }

        _eventSubscriptions.Clear();
    }

    #region Lifecycle

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        DisposeEventSubscriptions();

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

        await InitializeHeavyUiAsync();

        if (_settings.AutoConnectYouTube)
        {
            await TryAutoConnectYouTubeAsync();
        }

        UpdateLlmStatusText();
        UpdateLogLinkIndicator();
    }

    private async Task InitializeHeavyUiAsync()
    {
        try
        {
            await Task.WhenAll(
                LoadTemplatesAsync(),
                InitializeTranscriptionServiceAsync(),
                LoadUploadHistoryAsync());
        }
        catch (Exception ex)
        {
            _logger.Error($"Initialisierung fehlgeschlagen: {ex.Message}", "MainWindow", ex);
            StatusTextBlock.Text = $"Initialisierung fehlgeschlagen: {ex.Message}";
        }
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

        var languages = LocalizationManager.Instance.AvailableLanguages;
        var currentLang = _settings.Language ?? LocalizationManager.Instance.CurrentLanguage;
        SettingsPageView?.SetLanguageOptions(languages, currentLang);

        _isLanguageInitializing = false;
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLanguageInitializing)
        {
            return;
        }

        var selectedLang = SettingsPageView?.GetSelectedLanguage();
        if (selectedLang is null)
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
        TemplatesPageView?.SetPlatformOptions(Enum.GetValues(typeof(PlatformType)));

        UploadPageView?.SetDefaultVisibility(_settings.DefaultVisibility);
    }

    private void InitializeChannelLanguageComboBox()
    {
        var cultures = CultureInfo
            .GetCultures(CultureTypes.NeutralCultures)
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .OrderBy(c => c.DisplayName)
            .Select(c => $"{c.DisplayName} ({c.Name})")
            .ToList();

        ChannelPageView?.SetLanguageOptions(cultures);
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

        UploadView.ScheduleDatePicker.SelectedDate = DateTime.Today.AddDays(1);
        UploadView.ScheduleTimeTextBox.Text = defaultTime.ToString(@"hh\:mm");
        UpdateScheduleControlsEnabled();
    }

    private void InitializeLlmSettingsTab()
    {
        SettingsPageView?.ApplyLlmSettings(_settings.Llm ?? new LlmSettings());
        UpdateLlmControlsEnabled();
    }

    #endregion

    
    
    
    #region Content Generation Events

    private async void GenerateTitleButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();

        StatusTextBlock.Text = "Generiere Titelvorschläge...";
        UploadView.GenerateTitleButton.IsEnabled = false;

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
                UploadView.TitleTextBox.Text = "[Keine Vorschläge]";
                StatusTextBlock.Text = "Keine Titelvorschläge erhalten.";
                _logger.Warning("Keine Titelvorschläge erhalten", "LLM");
                return;
            }

            UploadView.TitleTextBox.Text = titles[0];
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
            UploadView.GenerateTitleButton.IsEnabled = true;
            UpdateLogLinkIndicator();
        }
    }

    private async void GenerateDescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();

        StatusTextBlock.Text = "Generiere Beschreibungsvorschlag...";
        UploadView.GenerateDescriptionButton.IsEnabled = false;

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
                UploadView.DescriptionTextBox.Text = description;
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
            UploadView.GenerateDescriptionButton.IsEnabled = true;
            UpdateLogLinkIndicator();
        }
    }

    private async void GenerateTagsButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();

        StatusTextBlock.Text = "Generiere Tag-Vorschläge...";
        UploadView.GenerateTagsButton.IsEnabled = false;

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
                UploadView.TagsTextBox.Text = string.Join(", ", tags);
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
            UploadView.GenerateTagsButton.IsEnabled = true;
            UpdateLogLinkIndicator();
        }
    }

    #endregion

    
    #region Accounts Service Tabs

    private void AccountsServiceTab_Checked(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag as string;
        var youTubeSelected = string.Equals(tag, "YouTube", StringComparison.OrdinalIgnoreCase);
        var tikTokSelected = string.Equals(tag, "TikTok", StringComparison.OrdinalIgnoreCase);
        var instagramSelected = string.Equals(tag, "Instagram", StringComparison.OrdinalIgnoreCase);

        SetAccountsServiceSelection(youTubeSelected, tikTokSelected, instagramSelected);
    }

    private void SetAccountsServiceSelection(bool youTubeSelected, bool tikTokSelected, bool instagramSelected)
    {
        AccountsPageView?.SetServiceSelection(youTubeSelected, tikTokSelected, instagramSelected);
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
        var transcript = UploadView.TranscriptTextBox.Text;
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
        if (!string.IsNullOrWhiteSpace(UploadView.VideoPathTextBox.Text))
        {
            baseName = Path.GetFileNameWithoutExtension(UploadView.VideoPathTextBox.Text);
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
}
