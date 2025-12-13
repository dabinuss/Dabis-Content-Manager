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
using System.Text;

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
    private const string MainWindowLogSource = "MainWindow";
    private const string SettingsLogSource = "Settings";
    private const string LlmLogSource = "LLM";
    private const string UpdatesLogSource = "Updates";
    private const string HistoryLogSource = "History";

    private ILlmClient _llmClient;
    private IContentSuggestionService _contentSuggestionService;
    private readonly UploadService _uploadService;
    private AppSettings _settings = new();

    private readonly List<Template> _loadedTemplates = new();
    private Template? _currentEditingTemplate;
    private Template? _lastAppliedTemplate;
    private bool _lastAppliedTemplateHasDescriptionPlaceholder;
    private string? _lastAppliedTemplateBaseDescription;
    private string? _lastAppliedTemplateResult;
    private bool _isTemplateBinding;
    private bool _isSettingDescriptionText;

    private readonly List<YouTubePlaylistInfo> _youTubePlaylists = new();

    private List<UploadHistoryEntry> _allHistoryEntries = new();

    private CancellationTokenSource? _currentLlmCts;

    private LogWindow? _logWindow;
    private bool _isClosing;
    private UploadView UploadView => UploadPageView ?? throw new InvalidOperationException("Upload view is not ready.");
    private readonly UiEventAggregator _eventAggregator = UiEventAggregator.Instance;
    private readonly List<IDisposable> _eventSubscriptions = new();
    private SuggestionTarget _activeSuggestionTarget = SuggestionTarget.None;
    private bool _isThemeInitializing;

    public MainWindow()
    {
        InitializeComponent();
        UpdateMaximizeButtonIcon();
        AttachUploadViewEvents();
        AttachAccountsViewEvents();
        AttachChannelViewEvents();
        AttachTemplatesViewEvents();
        AttachHistoryViewEvents();
        AttachSettingsViewEvents();
        RegisterEventSubscriptions();
        InitializeTemplatePlaceholders();

        AppVersion = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.0"}";

        _logger = AppLogger.Instance;
        _logger.Info(LocalizationHelper.Get("Log.MainWindow.Started"), MainWindowLogSource);

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
        InitializeThemeComboBox();
        InitializeChannelLanguageComboBox();
        InitializeSchedulingDefaults();
        InitializeLlmSettingsTab();
        UpdateYouTubeStatusText();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
    }

    #region Window Chrome

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2 &&
            (ResizeMode == ResizeMode.CanResize || ResizeMode == ResizeMode.CanResizeWithGrip))
        {
            ToggleWindowState();
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // Ignore drag failures when mouse is released early
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

        UpdateMaximizeButtonIcon();
    }

    private void UpdateMaximizeButtonIcon()
    {
        if (MaximizeIconTextBlock is null)
        {
            return;
        }

        MaximizeIconTextBlock.Text = WindowState == WindowState.Maximized
            ? "\uE5D1"
            : "\uE5D0";
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeButtonIcon();
    }

    #endregion

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
        UploadPageView.DescriptionTextBoxTextChanged += DescriptionTextBox_TextChanged;
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
        UploadPageView.SuggestionItemClicked += UploadView_SuggestionItemClicked;
        UploadPageView.SuggestionCloseButtonClicked += UploadView_SuggestionCloseButtonClicked;
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
        SettingsPageView.ThemeComboBoxSelectionChanged += ThemeComboBox_SelectionChanged;
        SettingsPageView.TranscriptionDownloadButtonClicked += TranscriptionDownloadButton_Click;
        SettingsPageView.TranscriptionModelSizeSelectionChanged += TranscriptionModelSizeComboBox_SelectionChanged;
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

        _logger.Info(LocalizationHelper.Get("Log.MainWindow.Closing"), MainWindowLogSource);

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
            _logger.Error(LocalizationHelper.Format("Log.MainWindow.InitializationFailed", ex.Message), MainWindowLogSource, ex);
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Main.InitializationFailed", ex.Message);
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

        StatusTextBlock.Text = LocalizationHelper.Format("Status.Main.LanguageChanged", selectedLang.DisplayName);
        _logger.Info(LocalizationHelper.Format("Log.Settings.LanguageChanged", selectedLang.Code), SettingsLogSource);
    }

    #endregion

    #region Theme Selection

    private void InitializeThemeComboBox()
    {
        if (SettingsPageView is null)
        {
            return;
        }

        _isThemeInitializing = true;
        var themeName = string.IsNullOrWhiteSpace(_settings.Theme) ? "Dark" : _settings.Theme.Trim();
        SettingsPageView.SetSelectedTheme(themeName);
        _isThemeInitializing = false;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isThemeInitializing)
        {
            return;
        }

        var selectedTheme = SettingsPageView?.GetSelectedTheme();
        if (string.IsNullOrWhiteSpace(selectedTheme))
        {
            return;
        }

        _settings.Theme = selectedTheme;
        ApplyTheme(selectedTheme);
        SaveSettings();

        StatusTextBlock.Text = $"Theme switched to {selectedTheme}.";
    }

    private void ApplyTheme(string? themeName)
    {
        var target = string.IsNullOrWhiteSpace(themeName) ? "Dark" : themeName.Trim();
        var themeUri = new Uri($"Themes/{target}Theme.xaml", UriKind.Relative);

        try
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var newThemeDictionary = new ResourceDictionary { Source = themeUri };

            var existingIndex = FindThemeDictionaryIndex(dictionaries);
            if (existingIndex >= 0)
            {
                dictionaries[existingIndex] = newThemeDictionary;
            }
            else
            {
                dictionaries.Add(newThemeDictionary);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to apply theme '{target}': {ex.Message}", SettingsLogSource, ex);
        }
    }

    private static int FindThemeDictionaryIndex(IList<ResourceDictionary> dictionaries)
    {
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains("/Themes/", StringComparison.OrdinalIgnoreCase) ||
                source.EndsWith("Theme.xaml", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
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

    private void InitializeTemplatePlaceholders()
    {
        var placeholders = new[]
        {
            "{Title}",
            "{Description}",
            "{Tags}",
            "{Hashtags}",
            "{Playlist}",
            "{Playlist_Id}",
            "{Visibility}",
            "{Platform}",
            "{ScheduledDate}",
            "{ScheduledTime}",
            "{Date}",
            "{CreatedAt}",
            "{VideoFile}",
            "{Transcript}"
        };

        TemplatesPageView?.SetPlaceholders(placeholders);
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
        CloseSuggestionPopup();

        CloseSuggestionPopup();

        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();

        StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.GenerateTitles");
        UploadView.GenerateTitleButton.IsEnabled = false;

        var cancellationToken = GetNewLlmCancellationToken();

        _logger.Debug(LocalizationHelper.Get("Log.LLM.TitleGeneration.Started"), LlmLogSource);

        try
        {
            var titles = await CollectSuggestionsAsync(
                () => _contentSuggestionService.SuggestTitlesAsync(project, _settings.Persona, cancellationToken),
                Math.Max(1, _settings.TitleSuggestionCount),
                maxRetries: 2,
                cancellationToken);

            if (titles is null || titles.Count == 0)
            {
                UploadView.TitleTextBox.Text = LocalizationHelper.Get("Llm.TitlePlaceholder.NoSuggestions");
                StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.NoTitles");
                _logger.Warning(LocalizationHelper.Get("Log.LLM.TitleGeneration.NoSuggestions"), LlmLogSource);
                return;
            }

            var desired = Math.Max(1, _settings.TitleSuggestionCount);
            var trimmed = titles.Take(Math.Max(1, desired)).ToList();

            if (desired <= 1 || trimmed.Count <= 1)
            {
                UploadView.TitleTextBox.Text = trimmed[0];
                StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TitleInserted", 1);
                _logger.Info(
                    LocalizationHelper.Format("Log.LLM.TitleGeneration.Success", trimmed[0]),
                    LlmLogSource);
            }
            else
            {
                ShowSuggestionPopup(SuggestionTarget.Title, trimmed, LocalizationHelper.Get("Upload.Fields.Title"));
                StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TitleInserted", trimmed.Count);
            }
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.TitleCanceled");
            _logger.Debug(LocalizationHelper.Get("Log.LLM.TitleGeneration.Canceled"), LlmLogSource);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TitleError", ex.Message);
            _logger.Error(
                LocalizationHelper.Format("Log.LLM.TitleGeneration.Error", ex.Message),
                LlmLogSource,
                ex);
        }
        finally
        {
            UploadView.GenerateTitleButton.IsEnabled = true;
            UpdateLogLinkIndicator();
        }
    }

    private async void GenerateDescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        CloseSuggestionPopup();

        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();

        StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.GenerateDescription");
        UploadView.GenerateDescriptionButton.IsEnabled = false;

        var cancellationToken = GetNewLlmCancellationToken();

        _logger.Debug(LocalizationHelper.Get("Log.LLM.DescriptionGeneration.Started"), LlmLogSource);

        try
        {
            var descriptions = await CollectSuggestionsAsync(
                () => _contentSuggestionService.SuggestDescriptionAsync(project, _settings.Persona, cancellationToken),
                Math.Max(1, _settings.DescriptionSuggestionCount),
                maxRetries: 2,
                cancellationToken);

            if (descriptions is null || descriptions.Count == 0)
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.NoDescriptions");
                _logger.Warning(LocalizationHelper.Get("Log.LLM.DescriptionGeneration.NoSuggestions"), LlmLogSource);
                return;
            }

            var desired = Math.Max(1, _settings.DescriptionSuggestionCount);
            var trimmed = descriptions.Take(Math.Max(1, desired)).ToList();

            if (desired <= 1 || trimmed.Count <= 1)
            {
                ApplyGeneratedDescription(trimmed[0]);
                StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.DescriptionInserted");
                _logger.Info(LocalizationHelper.Get("Log.LLM.DescriptionGeneration.Success"), LlmLogSource);
            }
            else
            {
                ShowSuggestionPopup(SuggestionTarget.Description, trimmed, LocalizationHelper.Get("Upload.Fields.Description"));
                StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.GenerateDescription");
            }
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.DescriptionCanceled");
            _logger.Debug(LocalizationHelper.Get("Log.LLM.DescriptionGeneration.Canceled"), LlmLogSource);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.DescriptionError", ex.Message);
            _logger.Error(
                LocalizationHelper.Format("Log.LLM.DescriptionGeneration.Error", ex.Message),
                LlmLogSource,
                ex);
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

        StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.GenerateTags");
        UploadView.GenerateTagsButton.IsEnabled = false;

        var cancellationToken = GetNewLlmCancellationToken();

        _logger.Debug(LocalizationHelper.Get("Log.LLM.TagGeneration.Started"), LlmLogSource);

        try
        {
            var tags = await _contentSuggestionService.SuggestTagsAsync(
                project,
                _settings.Persona,
                cancellationToken);

            if (tags is not null && tags.Count > 0)
            {
                var desired = Math.Max(1, _settings.TagsSuggestionCount);
                var suggestions = BuildTagSuggestionSets(tags, desired);

                if (desired <= 1 || suggestions.Count <= 1)
                {
                    var joined = suggestions.FirstOrDefault() ?? string.Join(", ", tags);
                    UploadView.TagsTextBox.Text = joined;
                    var count = joined.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                    StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TagsInserted", count);
                    _logger.Info(
                        LocalizationHelper.Format("Log.LLM.TagGeneration.Success", count),
                        LlmLogSource);
                }
                else
                {
                    ShowSuggestionPopup(SuggestionTarget.Tags, suggestions, LocalizationHelper.Get("Upload.Fields.Tags"));
                    StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TagsInserted", tags.Count);
                }
            }
            else
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.NoTags");
                _logger.Warning(LocalizationHelper.Get("Log.LLM.TagGeneration.NoSuggestions"), LlmLogSource);
            }
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.TagsCanceled");
            _logger.Debug(LocalizationHelper.Get("Log.LLM.TagGeneration.Canceled"), LlmLogSource);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TagsError", ex.Message);
            _logger.Error(
                LocalizationHelper.Format("Log.LLM.TagGeneration.Error", ex.Message),
                LlmLogSource,
                ex);
        }
        finally
        {
            UploadView.GenerateTagsButton.IsEnabled = true;
            UpdateLogLinkIndicator();
        }
    }

    private void UploadView_SuggestionItemClicked(object? sender, string suggestion)
    {
        switch (_activeSuggestionTarget)
        {
            case SuggestionTarget.Title:
                UploadView.TitleTextBox.Text = suggestion;
                StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TitleInserted", 1);
                break;
            case SuggestionTarget.Description:
                ApplyGeneratedDescription(suggestion);
                StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.DescriptionInserted");
                break;
            case SuggestionTarget.Tags:
                UploadView.TagsTextBox.Text = suggestion;
                var tagCount = suggestion.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TagsInserted", tagCount);
                break;
        }

        CloseSuggestionPopup();
    }

    private void UploadView_SuggestionCloseButtonClicked(object? sender, EventArgs e) => CloseSuggestionPopup();

    private void ShowSuggestionPopup(SuggestionTarget target, IReadOnlyList<string> suggestions, string title)
    {
        _activeSuggestionTarget = target;
        UploadView.ShowSuggestionOverlay(title, suggestions);
    }

    private void CloseSuggestionPopup()
    {
        _activeSuggestionTarget = SuggestionTarget.None;
        UploadView.HideSuggestionOverlay();
    }

    private static async Task<List<string>> CollectSuggestionsAsync(
        Func<Task<IReadOnlyList<string>>> producer,
        int desiredCount,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        desiredCount = Math.Max(1, desiredCount);
        maxRetries = Math.Max(0, maxRetries);

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        var attempts = 0;
        while (attempts == 0 || (attempts <= maxRetries && ordered.Count < desiredCount))
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<string> batch;
            try
            {
                batch = await producer();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                break;
            }

            if (batch is not null)
            {
                foreach (var item in batch)
                {
                    if (string.IsNullOrWhiteSpace(item))
                    {
                        continue;
                    }

                    if (unique.Add(item))
                    {
                        ordered.Add(item);

                        if (ordered.Count >= desiredCount)
                        {
                            break;
                        }
                    }
                }
            }

            attempts++;
        }

        return ordered;
    }

    private static List<string> BuildTagSuggestionSets(IReadOnlyList<string> tags, int desiredCount)
    {
        var cleaned = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count == 0)
        {
            return new List<string>();
        }

        desiredCount = Math.Max(1, desiredCount);
        if (desiredCount == 1)
        {
            return new List<string> { string.Join(", ", cleaned) };
        }

        var results = new List<string>();
        var groupSize = Math.Max(5, (int)Math.Ceiling(cleaned.Count / (double)desiredCount));

        for (var i = 0; i < cleaned.Count && results.Count < desiredCount; i += groupSize)
        {
            var slice = cleaned.Skip(i).Take(groupSize).ToList();
            if (slice.Count > 0)
            {
                results.Add(string.Join(", ", slice));
            }
        }

        if (results.Count == 0)
        {
            results.Add(string.Join(", ", cleaned));
        }

        return results;
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
            _logger.Error(LocalizationHelper.Format("Log.MainWindow.LogWindowOpenFailed", ex.Message), MainWindowLogSource, ex);
            MessageBox.Show(
                this,
                LocalizationHelper.Format("Dialog.LogWindow.OpenFailed.Text", ex.Message),
                LocalizationHelper.Get("Dialog.LogWindow.OpenFailed.Title"),
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
                LogLinkTextBlock.Text = LocalizationHelper.Format("Log.Link.WithErrors", _logger.ErrorCount);
                LogLinkTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0x4E, 0x45));
            }
            else
            {
                LogLinkTextBlock.Text = LocalizationHelper.Get("Log.Link.Default");
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
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Update.Checking");

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DabisContentManager/1.0");

            using var response = await httpClient.GetAsync("https://api.github.com/repos/dabinuss/Dabis-Content-Manager/releases/latest");
            if (!response.IsSuccessStatusCode)
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Update.NotPossible");
                _logger.Warning(
                    LocalizationHelper.Format("Log.Updates.HttpFailure", (int)response.StatusCode),
                    UpdatesLogSource);
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
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Update.InfoMissing");
                _logger.Warning(LocalizationHelper.Get("Log.Updates.NoTag"), UpdatesLogSource);
                return;
            }

            var latestVersionString = tagName.TrimStart('v', 'V');
            if (!Version.TryParse(latestVersionString, out var latestVersion))
            {
                StatusTextBlock.Text = LocalizationHelper.Format("Status.Update.VersionFormat", tagName);
                _logger.Warning(
                    LocalizationHelper.Format("Log.Updates.VersionParseFailed", tagName),
                    UpdatesLogSource);
                return;
            }

            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
                                ?? new Version(0, 0);

            if (latestVersion <= currentVersion)
            {
                StatusTextBlock.Text = LocalizationHelper.Format("Status.Update.LatestVersion", AppVersion);
                _logger.Info(
                    LocalizationHelper.Format("Log.Updates.NoNewVersion", currentVersion, latestVersion),
                    UpdatesLogSource);
                return;
            }

            StatusTextBlock.Text = LocalizationHelper.Format("Status.Update.NewVersion", latestVersion);
            _logger.Info(
                LocalizationHelper.Format("Log.Updates.NewVersionFound", currentVersion, latestVersion),
                UpdatesLogSource);

            var result = MessageBox.Show(
                this,
                LocalizationHelper.Format("Dialog.Update.Available.Text", currentVersion, latestVersion),
                LocalizationHelper.Get("Dialog.Update.Available.Title"),
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
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Update.Failed", ex.Message);
            _logger.Error(
                LocalizationHelper.Format("Log.Updates.CheckFailed", ex.Message),
                UpdatesLogSource,
                ex);
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

    private async void TranscriptionExportButton_Click(object sender, RoutedEventArgs e)
    {
        var transcript = UploadView.TranscriptTextBox.Text;
        if (string.IsNullOrWhiteSpace(transcript))
        {
            MessageBox.Show(
                this,
                LocalizationHelper.Get("Dialog.Transcript.None.Text"),
                LocalizationHelper.Get("Dialog.Transcript.None.Title"),
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
            baseName = LocalizationHelper.Get("Transcription.Export.DefaultFileName");
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = LocalizationHelper.Get("Dialog.Transcript.Export.Title"),
            FileName = $"{baseName}_transcript.txt",
            Filter = LocalizationHelper.Get("Dialog.Transcript.Export.Filter")
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
            await File.WriteAllTextAsync(dialog.FileName, transcript, Encoding.UTF8);
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Transcription.ExportSuccess", dialog.FileName);
            _logger.Info(LocalizationHelper.Format("Log.Transcription.ExportSuccess", dialog.FileName), TranscriptionLogSource);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Transcription.ExportFailed", ex.Message);
            _logger.Error(
                LocalizationHelper.Format("Log.Transcription.ExportFailed", ex.Message),
                TranscriptionLogSource,
                ex);

            MessageBox.Show(
                this,
                LocalizationHelper.Format("Dialog.Transcript.Export.Error.Text", ex.Message),
                LocalizationHelper.Get("Dialog.Transcript.Export.Error.Title"),
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

    private enum SuggestionTarget
    {
        None,
        Title,
        Description,
        Tags
    }
}
