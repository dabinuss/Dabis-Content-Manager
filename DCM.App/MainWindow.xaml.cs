using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using DCM.App.Models;
using DCM.App.Views;
using DCM.App.Services;
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
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;

namespace DCM.App;

public partial class MainWindow : Window
{
    public string AppVersion { get; set; }
    private readonly TemplateService _templateService;
    private readonly IPresetRepository _presetRepository;
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

    private readonly List<UploadPreset> _loadedPresets = new();
    private UploadPreset? _currentEditingPreset;
    private readonly PresetApplicationState _presetState = new();
    private bool _isPresetBinding;
    private bool _isSettingDescriptionText;

    private readonly List<YouTubePlaylistInfo> _youTubePlaylists = new();
    private readonly List<CategoryOption> _youTubeCategories = new();
    private readonly List<LanguageOption> _youTubeLanguages = new();

    private List<UploadHistoryEntry> _allHistoryEntries = new();

    private CancellationTokenSource? _currentLlmCts;
    private bool _isChaptersGenerationRunning;

    private LogWindow? _logWindow;
    private bool _isClosing;
    private UploadView UploadView => UploadPageView ?? throw new InvalidOperationException("Upload view is not ready.");
    private readonly UiEventAggregator _eventAggregator = UiEventAggregator.Instance;
    private readonly List<IDisposable> _eventSubscriptions = new();
    private SuggestionTarget _activeSuggestionTarget = SuggestionTarget.None;
    private bool _isThemeInitializing;
    private bool _isUploadLanguageInitializing;
    private readonly ObservableCollection<UploadDraft> _uploadDrafts = new();
    private readonly List<Guid> _transcriptionQueue = new();
    private UploadDraft? _activeDraft;
    private bool _isLoadingDraft;
    private readonly DraftRepository _draftRepository = new();
    private readonly DraftPersistenceCoordinator _draftPersistence;
    private DispatcherTimer? _settingsSaveTimer;
    private bool _settingsSaveDirty;
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private int _settingsSaveVersion;
    private int _settingsSaveWrittenVersion;
    private bool _isRestoringDrafts;
    private bool _isUploading;
    private UploadDraft? _activeUploadDraft;
    private CancellationTokenSource? _activeUploadCts;
    private int _currentPageIndex;
    private int _lastUploadsPageIndex = 0;
    private int _lastConnectionsPageIndex = 1;
    private int _lastSettingsPageIndex = 4;

    // OPTIMIZATION: Cached theme dictionary for faster theme switching
    private ResourceDictionary? _currentThemeDictionary;

    public MainWindow()
    {
        InitializeComponent();
        _currentPageIndex = 0;
        UpdatePageHeader(_currentPageIndex);
        UpdatePageActions(_currentPageIndex);
        UpdateMaximizeButtonIcon();
        _draftPersistence = new DraftPersistenceCoordinator(
            Dispatcher,
            TimeSpan.FromSeconds(2),
            DraftTextDebounceDelay,
            () => _settings.RememberDraftsBetweenSessions == true,
            () => _isRestoringDrafts,
            PersistDrafts);

        AttachUploadViewEvents();
        AttachAccountsViewEvents();
        AttachPresetsViewEvents();
        AttachHistoryViewEvents();
        AttachSettingsViewEvents();
        AttachChannelProfileViewEvents();
        RegisterEventSubscriptions();
        InitializePresetPlaceholders();
        InitializePresetOptions();
        InitializeUploadDrafts();

        AppVersion = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.0"}";

        _logger = AppLogger.Instance;
        _logger.Info(LocalizationHelper.Get("Log.MainWindow.Started"), MainWindowLogSource);

        // Fensterzustand wiederherstellen (smart, ohne das Fenster zu "verlieren")
        WindowStateManager.Apply(this);

        _templateService = new TemplateService(_logger);
        _settingsProvider = new JsonSettingsProvider(_logger);
        _presetRepository = new JsonPresetRepository(_logger);
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
        catch (Exception ex)
        {
            // OPTIMIZATION: Added logging for debugging
            _logger.Debug($"DragMove failed (non-critical): {ex.Message}", MainWindowLogSource);
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
        UploadPageView.AddVideosButtonClicked += AddVideosButton_Click;
        UploadPageView.TranscribeAllButtonClicked += TranscribeAllButton_Click;

        UploadPageView.VideoChangeButtonClicked += VideoChangeButton_Click;
        UploadPageView.UploadButtonClicked += UploadButton_Click;
        UploadPageView.TitleTextBoxTextChanged += TitleTextBox_TextChanged;
        UploadPageView.DescriptionTextBoxTextChanged += DescriptionTextBox_TextChanged;
        UploadPageView.TagsTextBoxTextChanged += TagsTextBox_TextChanged;
        UploadPageView.TranscriptTextBoxTextChanged += TranscriptTextBox_TextChanged;
        UploadPageView.ChaptersTextBoxTextChanged += ChaptersTextBox_TextChanged;
        UploadPageView.GenerateTitleButtonClicked += GenerateTitleButton_Click;
        UploadPageView.GenerateDescriptionButtonClicked += GenerateDescriptionButton_Click;
        UploadPageView.GenerateChaptersButtonClicked += GenerateChaptersButton_Click;
        UploadPageView.PresetComboBoxSelectionChanged += PresetComboBox_SelectionChanged;
        UploadPageView.ApplyPresetButtonClicked += ApplyPresetButton_Click;
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
        UploadPageView.PlatformYouTubeToggleChecked += PlatformYouTubeToggle_Checked;
        UploadPageView.PlatformYouTubeToggleUnchecked += PlatformYouTubeToggle_Unchecked;
        UploadPageView.VisibilitySelectionChanged += VisibilityComboBox_SelectionChanged;
        UploadPageView.PlaylistSelectionChanged += PlaylistComboBox_SelectionChanged;
        UploadPageView.CategoryIdTextBoxTextChanged += CategoryIdTextBox_TextChanged;
        UploadPageView.LanguageSelectionChanged += UploadLanguageComboBox_SelectionChanged;
        UploadPageView.MadeForKidsSelectionChanged += MadeForKidsComboBox_SelectionChanged;
        UploadPageView.CommentStatusSelectionChanged += CommentStatusComboBox_SelectionChanged;
        UploadPageView.ScheduleCheckBoxChecked += ScheduleCheckBox_Checked;
        UploadPageView.ScheduleCheckBoxUnchecked += ScheduleCheckBox_Unchecked;
        UploadPageView.ScheduleDateChanged += ScheduleDatePicker_SelectedDateChanged;
        UploadPageView.ScheduleTimeTextChanged += ScheduleTimeTextBox_TextChanged;
        UploadPageView.UploadItemsSelectionChanged += UploadDraftSelectionChanged;
        UploadPageView.RemoveDraftButtonClicked += RemoveDraftButton_Click;
        UploadPageView.FastFillSuggestionsButtonClicked += FastFillSuggestionsButton_Click;
        UploadPageView.UploadDraftButtonClicked += UploadDraftButton_Click;
        UploadPageView.TranscribeDraftButtonClicked += TranscribeDraftButton_Click;
        UploadPageView.CancelUploadButtonClicked += CancelUploadButton_Click;
        UploadPageView.TranscriptionPrioritizeButtonClicked += TranscriptionQueuePrioritizeButton_Click;
        UploadPageView.TranscriptionSkipButtonClicked += TranscriptionQueueSkipButton_Click;
    }

    private void InitializeUploadDrafts()
    {
        if (UploadPageView is null)
        {
            return;
        }

        UploadView.SetUploadItemsSource(_uploadDrafts);
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
        AccountsPageView.YouTubeAutoConnectToggled += YouTubeAutoConnectToggled;
        AccountsPageView.YouTubeRefreshButtonClicked += YouTubeRefreshButton_Click;
        AccountsPageView.YouTubeOpenLogsButtonClicked += YouTubeOpenLogsButton_Click;
    }

    private void AttachChannelProfileViewEvents()
    {
        if (ChannelProfilePageView is null)
        {
            return;
        }

        ChannelProfilePageView.ChannelProfileSaveButtonClicked += ChannelProfileSaveButton_Click;
    }

    private void AttachPresetsViewEvents()
    {
        if (PresetsPageView is null)
        {
            return;
        }

        PresetsPageView.PresetNewButtonClicked += PresetNewButton_Click;
        PresetsPageView.PresetEditButtonClicked += PresetEditButton_Click;
        PresetsPageView.PresetDeleteButtonClicked += PresetDeleteButton_Click;
        PresetsPageView.PresetSaveButtonClicked += PresetSaveButton_Click;
        PresetsPageView.PresetListBoxSelectionChanged += PresetListBox_SelectionChanged;
        PresetsPageView.PresetDefaultToggleRequested += PresetDefaultToggleRequested;
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
        if (GeneralSettingsPageView is not null)
        {
            GeneralSettingsPageView.SettingsSaveButtonClicked += SettingsSaveButton_Click;
            GeneralSettingsPageView.DefaultVideoFolderBrowseButtonClicked += DefaultVideoFolderBrowseButton_Click;
            GeneralSettingsPageView.DefaultThumbnailFolderBrowseButtonClicked += DefaultThumbnailFolderBrowseButton_Click;
            GeneralSettingsPageView.LanguageComboBoxSelectionChanged += LanguageComboBox_SelectionChanged;
            GeneralSettingsPageView.ThemeComboBoxSelectionChanged += ThemeComboBox_SelectionChanged;
            GeneralSettingsPageView.TranscriptionDownloadButtonClicked += TranscriptionDownloadButton_Click;
            GeneralSettingsPageView.TranscriptionModelSizeSelectionChanged += TranscriptionModelSizeComboBox_SelectionChanged;
        }

        if (LlmSettingsPageView is not null)
        {
            LlmSettingsPageView.SettingsSaveButtonClicked += SettingsSaveButton_Click;
            LlmSettingsPageView.LlmModeComboBoxSelectionChanged += LlmModeComboBox_SelectionChanged;
            LlmSettingsPageView.LlmModelPathBrowseButtonClicked += LlmModelPathBrowseButton_Click;
        }
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

    private void OnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.BeginInvoke(action);
    }

    private Task OnUiThreadAsync(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.InvokeAsync(action).Task;
    }

    #region Lifecycle

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        DisposeEventSubscriptions();

        // Fensterzustand speichern
        WindowStateManager.Save(this);
        _draftPersistence.FlushIfDirty();
        _settingsSaveTimer?.Stop();
        _settingsSaveTimer = null;
        if (_settingsSaveDirty)
        {
            _settingsSaveDirty = false;
            SaveSettings(forceSync: true);
        }

        // Event-Handler zuerst entfernen
        try
        {
            _logger.EntryAdded -= OnLogEntryAdded;
        }
        catch (Exception ex)
        {
            // OPTIMIZATION: Added logging
            _logger.Debug($"Failed to remove log event handler: {ex.Message}", MainWindowLogSource);
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
            catch (Exception ex)
            {
                // OPTIMIZATION: Added logging
                _logger.Debug($"Failed to dispose LLM client: {ex.Message}", MainWindowLogSource);
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
        catch (Exception ex)
        {
            // OPTIMIZATION: Added logging
            _logger.Debug($"Failed to close log window: {ex.Message}", MainWindowLogSource);
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Event-Handler für Log-Updates registrieren (nach UI-Initialisierung)
        _logger.EntryAdded += OnLogEntryAdded;

        await InitializeHeavyUiAsync().ConfigureAwait(true); // OPTIMIZATION: Keep UI context

        if (_settings.AutoConnectYouTube)
        {
            await TryAutoConnectYouTubeAsync().ConfigureAwait(true); // OPTIMIZATION: Keep UI context
        }

        UpdateLlmStatusText();
        UpdateLogLinkIndicator();
    }

    private async Task InitializeHeavyUiAsync()
    {
        try
        {
            await Task.WhenAll(
                LoadPresetsAsync(),
                InitializeTranscriptionServiceAsync(),
                LoadUploadHistoryAsync()).ConfigureAwait(true); // OPTIMIZATION: Keep UI context

            await RefreshDraftVideoInfoAsync().ConfigureAwait(true);
            await RefreshDraftVideoPreviewAsync().ConfigureAwait(true);
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
                // OPTIMIZATION: Use InvokeAsync with lower priority for better responsiveness
                Dispatcher.InvokeAsync(() =>
                {
                    if (!_isClosing)
                    {
                        UpdateLogLinkIndicatorSafe();
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                // OPTIMIZATION: Added logging
                _logger.Debug($"Dispatcher invoke failed: {ex.Message}", MainWindowLogSource);
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
        catch (Exception ex)
        {
            // OPTIMIZATION: Added logging
            _logger.Debug($"Failed to update log link indicator: {ex.Message}", MainWindowLogSource);
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
        GeneralSettingsPageView?.SetLanguageOptions(languages, currentLang);

        _isLanguageInitializing = false;
    }

    private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLanguageInitializing)
        {
            return;
        }

        var selectedLang = GeneralSettingsPageView?.GetSelectedLanguage();
        if (selectedLang is null)
        {
            return;
        }

        // Sprache wechseln
        LocalizationManager.Instance.SetLanguage(selectedLang.Code);

        // In Settings speichern
        _settings.Language = selectedLang.Code;
        ScheduleSettingsSave();

        StatusTextBlock.Text = LocalizationHelper.Format("Status.Main.LanguageChanged", selectedLang.DisplayName);
        UpdatePageHeader(_currentPageIndex);
        _logger.Info(LocalizationHelper.Format("Log.Settings.LanguageChanged", selectedLang.Code), SettingsLogSource);

        await RefreshYouTubeMetadataOptionsAsync().ConfigureAwait(true);
    }

    #endregion

    #region Theme Selection

    private void InitializeThemeComboBox()
    {
        if (GeneralSettingsPageView is null)
        {
            return;
        }

        _isThemeInitializing = true;
        var themeName = string.IsNullOrWhiteSpace(_settings.Theme) ? "Dark" : _settings.Theme.Trim();
        GeneralSettingsPageView.SetSelectedTheme(themeName);
        _isThemeInitializing = false;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isThemeInitializing)
        {
            return;
        }

        var selectedTheme = GeneralSettingsPageView?.GetSelectedTheme();
        if (string.IsNullOrWhiteSpace(selectedTheme))
        {
            return;
        }

        _settings.Theme = selectedTheme;
        ApplyTheme(selectedTheme);
        ScheduleSettingsSave();

        StatusTextBlock.Text = $"Theme switched to {selectedTheme}.";
    }

    private void ApplyTheme(string? themeName)
    {
        var target = string.IsNullOrWhiteSpace(themeName) ? "Dark" : themeName.Trim();
        var themeUri = new Uri($"Themes/{target}Theme.xaml", UriKind.Relative);

        try
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            
            // OPTIMIZATION: Remove cached theme dictionary if it exists
            if (_currentThemeDictionary != null)
            {
                dictionaries.Remove(_currentThemeDictionary);
            }

            // OPTIMIZATION: Create and cache new theme dictionary
            _currentThemeDictionary = new ResourceDictionary { Source = themeUri };
            dictionaries.Add(_currentThemeDictionary);
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
        catch (Exception ex)
        {
            // OPTIMIZATION: Added logging
            _logger.Debug($"Failed to cancel LLM operation: {ex.Message}", LlmLogSource);
        }
        finally
        {
            _currentLlmCts = null;
        }
    }

    // OPTIMIZATION: Added timeout parameter to prevent hanging operations
    private CancellationToken GetNewLlmCancellationToken(int timeoutSeconds = 300)
    {
        CancelCurrentLlmOperation();
        _currentLlmCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        return _currentLlmCts.Token;
    }

    private string ResolveSuggestionLanguage()
    {
        var appLanguage = _settings.Language;
        if (!string.IsNullOrWhiteSpace(appLanguage))
        {
            return appLanguage;
        }

        var currentUiLanguage = LocalizationManager.Instance.CurrentLanguage;
        if (!string.IsNullOrWhiteSpace(currentUiLanguage))
        {
            return currentUiLanguage;
        }

        return "en";
    }

    private void ApplySuggestionLanguage(UploadProject project)
    {
        if (project is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(project.Language))
        {
            project.Language = ResolveSuggestionLanguage();
        }
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
            catch (Exception ex)
            {
                // OPTIMIZATION: Added logging
                _logger.Debug($"Failed to dispose LLM client: {ex.Message}", LlmLogSource);
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
        // Nur noch PresetPlatformComboBox initialisieren
        PresetsPageView?.SetPlatformOptions(Enum.GetValues(typeof(PlatformType)));

        UploadPageView?.SetDefaultVisibility(_settings.DefaultVisibility);
    }

    private void InitializePresetPlaceholders()
    {
        var placeholders = new[]
        {
            "{Title}",
            "{Description}",
            "{Chapters}",
            "{Tags}",
            "{Hashtags}",
            "{Playlist}",
            "{Playlist_Id}",
            "{Visibility}",
            "{Platform}",
            "{Category}",
            "{Language}",
            "{MadeForKids}",
            "{CommentStatus}",
            "{ScheduledDate}",
            "{ScheduledTime}",
            "{Date}",
            "{CreatedAt}",
            "{VideoFile}",
            "{Transcript}"
        };

        PresetsPageView?.SetPlaceholders(placeholders);
    }

    private void InitializePresetOptions()
    {
        ApplyCachedYouTubeOptions();
    }

    private void ApplyCachedYouTubeOptions()
    {
        LoadCachedYouTubeOptions();
        PresetsPageView?.SetCategoryOptions(_youTubeCategories);
        PresetsPageView?.SetLanguageOptions(_youTubeLanguages);
        ApplyUploadLanguageOptions(_youTubeLanguages);
    }

    private void ApplyUploadLanguageOptions(IEnumerable<LanguageOption> languages)
    {
        _isUploadLanguageInitializing = true;
        try
        {
            UploadView.SetLanguageOptions(languages);
            UploadView.SelectLanguageByCode(_activeDraft?.Language);
        }
        finally
        {
            _isUploadLanguageInitializing = false;
        }
    }

    private void LoadCachedYouTubeOptions()
    {
        _youTubeCategories.Clear();
        _youTubeLanguages.Clear();

        var localeKey = GetCurrentLocaleKey();
        if (!string.Equals(_settings.YouTubeOptionsLocale, localeKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_settings.YouTubeCategoryOptions is not null)
        {
            foreach (var option in _settings.YouTubeCategoryOptions)
            {
                if (!string.IsNullOrWhiteSpace(option.Code))
                {
                    _youTubeCategories.Add(new CategoryOption(option.Code, option.Name));
                }
            }
        }

        if (_settings.YouTubeLanguageOptions is not null)
        {
            foreach (var option in _settings.YouTubeLanguageOptions)
            {
                if (!string.IsNullOrWhiteSpace(option.Code))
                {
                    _youTubeLanguages.Add(new LanguageOption(option.Code, option.Name));
                }
            }
        }
    }

    private string GetCurrentLocaleKey() =>
        _settings.Language ?? LocalizationManager.Instance.CurrentLanguage;

    private (string LocaleKey, string Hl, string RegionCode) GetYouTubeLocaleInfo()
    {
        var localeKey = GetCurrentLocaleKey();
        var hl = "en";
        var region = "US";

        try
        {
            var culture = CultureInfo.GetCultureInfo(localeKey);
            if (!string.IsNullOrWhiteSpace(culture.TwoLetterISOLanguageName))
            {
                hl = culture.TwoLetterISOLanguageName;
            }

            if (culture.Name.Contains('-', StringComparison.Ordinal))
            {
                region = culture.Name.Split('-')[1];
            }
            else
            {
                try
                {
                    var regionInfo = new RegionInfo(culture.Name);
                    region = regionInfo.TwoLetterISORegionName;
                }
                catch
                {
                    // Fallback below.
                }
            }
        }
        catch
        {
            // Use defaults.
        }

        if (string.IsNullOrWhiteSpace(region))
        {
            region = string.Equals(hl, "de", StringComparison.OrdinalIgnoreCase) ? "DE" : "US";
        }

        return (localeKey, hl, region);
    }

    private void InitializeChannelLanguageComboBox()
    {
        // OPTIMIZATION: Materialize the query once to avoid multiple enumerations
        var cultures = CultureInfo
            .GetCultures(CultureTypes.NeutralCultures)
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => new { c.DisplayName, c.Name })
            .OrderBy(c => c.DisplayName)
            .Select(c => $"{c.DisplayName} ({c.Name})")
            .ToList();

        ChannelProfilePageView?.SetLanguageOptions(cultures);
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
        LlmSettingsPageView?.ApplyLlmSettings(_settings.Llm ?? new LlmSettings());
        UpdateLlmControlsEnabled();
    }

    #endregion

    
    
    
    #region Content Generation Events

    // OPTIMIZATION: Extracted async logic to separate method with proper exception handling
    private async void GenerateTitleButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await GenerateTitleAsync().ConfigureAwait(true); // Keep UI context
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error in title generation: {ex.Message}", LlmLogSource, ex);
            StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TitleError", ex.Message);
            UploadView.GenerateTitleButton.IsEnabled = true;
            UpdateLogLinkIndicator();
        }
    }

    private async Task GenerateTitleAsync()
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();
        ApplySuggestionLanguage(project);

        var desired = Math.Max(1, _settings.TitleSuggestionCount);
        await RunSuggestionAsync(
            target: SuggestionTarget.Title,
            triggerButton: UploadView.GenerateTitleButton,
            desiredCount: desired,
            fieldLabel: LocalizationHelper.Get("Upload.Fields.Title"),
            statusGenerating: LocalizationHelper.Get("Status.LLM.GenerateTitles"),
            statusNoSuggestions: LocalizationHelper.Get("Status.LLM.NoTitles"),
            statusCanceled: LocalizationHelper.Get("Status.LLM.TitleCanceled"),
            statusError: ex => LocalizationHelper.Format("Status.LLM.TitleError", ex.Message),
            logStarted: () => _logger.Debug(LocalizationHelper.Get("Log.LLM.TitleGeneration.Started"), LlmLogSource),
            logNoSuggestions: () => _logger.Warning(LocalizationHelper.Get("Log.LLM.TitleGeneration.NoSuggestions"), LlmLogSource),
            logCanceled: () => _logger.Debug(LocalizationHelper.Get("Log.LLM.TitleGeneration.Canceled"), LlmLogSource),
            logError: ex => _logger.Error(
                LocalizationHelper.Format("Log.LLM.TitleGeneration.Error", ex.Message),
                LlmLogSource,
                ex),
            producer: ct => CollectSuggestionsAsync(
                () => _contentSuggestionService.SuggestTitlesAsync(project, _settings.Persona, ct),
                desired,
                maxRetries: 2,
                ct),
            suggestionSelector: items => (items ?? new List<string>())
                .Select(TextCleaningUtility.RemoveWrappedTitleQuotes)
                .Select(t => t?.Trim() ?? string.Empty)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList(),
            handleResults: (_, suggestions) =>
            {
                var trimmed = suggestions.Take(desired).ToList();
                if (trimmed.Count == 0)
                {
                    UploadView.TitleTextBox.Text = LocalizationHelper.Get("Llm.TitlePlaceholder.NoSuggestions");
                    StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.NoTitles");
                    _logger.Warning(LocalizationHelper.Get("Log.LLM.TitleGeneration.NoSuggestions"), LlmLogSource);
                    CloseSuggestionPopup();
                    return;
                }

                if (desired <= 1 || trimmed.Count <= 1)
                {
                    UploadView.TitleTextBox.Text = trimmed[0];
                    StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TitleInserted", 1);
                    _logger.Info(
                        LocalizationHelper.Format("Log.LLM.TitleGeneration.Success", trimmed[0]),
                        LlmLogSource);
                    CloseSuggestionPopup();
                }
                else
                {
                    ShowSuggestionPopup(SuggestionTarget.Title, trimmed, LocalizationHelper.Get("Upload.Fields.Title"));
                    StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TitleInserted", trimmed.Count);
                }
            },
            onNoSuggestions: () =>
            {
                UploadView.TitleTextBox.Text = LocalizationHelper.Get("Llm.TitlePlaceholder.NoSuggestions");
            }).ConfigureAwait(true); // Keep UI context
    }

    // OPTIMIZATION: Extracted async logic to separate method with proper exception handling
    private async void GenerateDescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await GenerateDescriptionAsync().ConfigureAwait(true); // Keep UI context
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error in description generation: {ex.Message}", LlmLogSource, ex);
            StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.DescriptionError", ex.Message);
            UploadView.GenerateDescriptionButton.IsEnabled = true;
            UpdateLogLinkIndicator();
        }
    }

    private async Task GenerateDescriptionAsync()
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();
        ApplySuggestionLanguage(project);

        var desired = Math.Max(1, _settings.DescriptionSuggestionCount);
        await RunSuggestionAsync(
            target: SuggestionTarget.Description,
            triggerButton: UploadView.GenerateDescriptionButton,
            desiredCount: desired,
            fieldLabel: LocalizationHelper.Get("Upload.Fields.Description"),
            statusGenerating: LocalizationHelper.Get("Status.LLM.GenerateDescription"),
            statusNoSuggestions: LocalizationHelper.Get("Status.LLM.NoDescriptions"),
            statusCanceled: LocalizationHelper.Get("Status.LLM.DescriptionCanceled"),
            statusError: ex => LocalizationHelper.Format("Status.LLM.DescriptionError", ex.Message),
            logStarted: () => _logger.Debug(LocalizationHelper.Get("Log.LLM.DescriptionGeneration.Started"), LlmLogSource),
            logNoSuggestions: () => _logger.Warning(LocalizationHelper.Get("Log.LLM.DescriptionGeneration.NoSuggestions"), LlmLogSource),
            logCanceled: () => _logger.Debug(LocalizationHelper.Get("Log.LLM.DescriptionGeneration.Canceled"), LlmLogSource),
            logError: ex => _logger.Error(
                LocalizationHelper.Format("Log.LLM.DescriptionGeneration.Error", ex.Message),
                LlmLogSource,
                ex),
            producer: ct => CollectSuggestionsAsync(
                () => _contentSuggestionService.SuggestDescriptionAsync(project, _settings.Persona, ct),
                desired,
                maxRetries: 2,
                ct),
            suggestionSelector: items => (items ?? new List<string>())
                .Select(t => t?.Trim() ?? string.Empty)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList(),
            handleResults: (_, suggestions) =>
            {
                var trimmed = suggestions.Take(desired).ToList();
                if (desired <= 1 || trimmed.Count <= 1)
                {
                    ApplyGeneratedDescription(trimmed[0]);
                    StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.DescriptionInserted");
                    _logger.Info(LocalizationHelper.Get("Log.LLM.DescriptionGeneration.Success"), LlmLogSource);
                    CloseSuggestionPopup();
                }
                else
                {
                    ShowSuggestionPopup(SuggestionTarget.Description, trimmed, LocalizationHelper.Get("Upload.Fields.Description"));
                    StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.GenerateDescription");
                }
            }).ConfigureAwait(true); // Keep UI context
    }

    // OPTIMIZATION: Extracted async logic to separate method with proper exception handling
    private async void GenerateTagsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await GenerateTagsAsync().ConfigureAwait(true); // Keep UI context
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error in tags generation: {ex.Message}", LlmLogSource, ex);
            StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TagsError", ex.Message);
            UploadView.GenerateTagsButton.IsEnabled = true;
            UpdateLogLinkIndicator();
        }
    }

    private async Task GenerateTagsAsync()
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();
        ApplySuggestionLanguage(project);

        var desired = Math.Max(1, _settings.TagsSuggestionCount);
        await RunSuggestionAsync(
            target: SuggestionTarget.Tags,
            triggerButton: UploadView.GenerateTagsButton,
            desiredCount: desired,
            fieldLabel: LocalizationHelper.Get("Upload.Fields.Tags"),
            statusGenerating: LocalizationHelper.Get("Status.LLM.GenerateTags"),
            statusNoSuggestions: LocalizationHelper.Get("Status.LLM.NoTags"),
            statusCanceled: LocalizationHelper.Get("Status.LLM.TagsCanceled"),
            statusError: ex => LocalizationHelper.Format("Status.LLM.TagsError", ex.Message),
            logStarted: () => _logger.Debug(LocalizationHelper.Get("Log.LLM.TagGeneration.Started"), LlmLogSource),
            logNoSuggestions: () => _logger.Warning(LocalizationHelper.Get("Log.LLM.TagGeneration.NoSuggestions"), LlmLogSource),
            logCanceled: () => _logger.Debug(LocalizationHelper.Get("Log.LLM.TagGeneration.Canceled"), LlmLogSource),
            logError: ex => _logger.Error(
                LocalizationHelper.Format("Log.LLM.TagGeneration.Error", ex.Message),
                LlmLogSource,
                ex),
            producer: ct => _contentSuggestionService.SuggestTagsAsync(project, _settings.Persona, ct),
            suggestionSelector: items => BuildTagSuggestionSets(items ?? Array.Empty<string>(), desired),
            handleResults: (tags, suggestions) =>
            {
                if (desired <= 1 || suggestions.Count <= 1)
                {
                    var joined = suggestions.FirstOrDefault() ?? string.Join(", ", tags);
                    UploadView.TagsTextBox.Text = joined;
                    var count = joined.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                    StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TagsInserted", count);
                    _logger.Info(
                        LocalizationHelper.Format("Log.LLM.TagGeneration.Success", count),
                        LlmLogSource);
                    CloseSuggestionPopup();
                }
                else
                {
                    ShowSuggestionPopup(SuggestionTarget.Tags, suggestions, LocalizationHelper.Get("Upload.Fields.Tags"));
                    StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.TagsInserted", tags.Count);
                }
            }).ConfigureAwait(true); // Keep UI context
    }

    private async void GenerateChaptersButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isChaptersGenerationRunning)
        {
            CancelCurrentLlmOperation();
            return;
        }

        try
        {
            await GenerateChaptersAsync().ConfigureAwait(true); // Keep UI context
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error in chapters generation: {ex.Message}", LlmLogSource, ex);
            StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.ChaptersError", ex.Message);
            SetChaptersGenerationState(false);
            UpdateLogLinkIndicator();
        }
    }

    private async Task GenerateChaptersAsync()
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();
        ApplySuggestionLanguage(project);

        var transcript = project.TranscriptText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(transcript))
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.ChaptersMissingTranscript");
            return;
        }

        if (!TryParseDurationSeconds(_activeDraft?.VideoDuration, out var durationSeconds))
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.ChaptersMissingDuration");
            return;
        }

        SetChaptersGenerationState(true);
        StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.GenerateChapters");

        var timeoutSeconds = GetChapterTimeoutSeconds(transcript);
        var cancellationToken = GetNewLlmCancellationToken(timeoutSeconds);

        _logger.Debug("Kapitelgenerierung gestartet", LlmLogSource);

        try
        {
            var topics = await _contentSuggestionService.SuggestChaptersAsync(
                project,
                _settings.Persona,
                cancellationToken).ConfigureAwait(true); // Keep UI context

            if (topics is null || topics.Count == 0)
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.NoChapters");
                _logger.Warning("Kapitel: Keine Vorschlaege erhalten", LlmLogSource);
                return;
            }

            var maxChapters = GetMaxChapters(durationSeconds);
            var chapterText = await Task.Run(
                () => BuildChapterMarkersText(topics, transcript, durationSeconds, maxChapters, cancellationToken),
                cancellationToken).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(chapterText))
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.NoChapters");
                return;
            }

            ApplyGeneratedChapters(chapterText);
            StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.ChaptersInserted");
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.LLM.ChaptersCanceled");
            _logger.Debug("Kapitelgenerierung abgebrochen", LlmLogSource);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.LLM.ChaptersError", ex.Message);
            _logger.Error($"Kapitelgenerierung fehlgeschlagen: {ex.Message}", LlmLogSource, ex);
        }
        finally
        {
            SetChaptersGenerationState(false);
            UpdateLogLinkIndicator();
        }
    }

    private void SetChaptersGenerationState(bool isRunning)
    {
        _isChaptersGenerationRunning = isRunning;
        if (UploadView?.GenerateChaptersButton is not null)
        {
            UploadView.GenerateChaptersButton.Tag = isRunning ? "active" : "default";
            UploadView.GenerateChaptersButton.IsEnabled = true;
        }
    }

    private static int GetChapterTimeoutSeconds(string transcript)
    {
        var length = Math.Max(0, transcript?.Length ?? 0);
        var scaled = 120 + (length / 200);
        return Math.Clamp(scaled, 180, 900);
    }

    private static bool TryParseDurationSeconds(string? durationText, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(durationText))
        {
            return false;
        }

        var parts = durationText.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 3)
        {
            return false;
        }

        var start = parts.Length == 3 ? 1 : 0;
        var hours = 0;
        if (parts.Length == 3 && !int.TryParse(parts[0], out hours))
        {
            return false;
        }

        if (!int.TryParse(parts[start], out var minutes) ||
            !int.TryParse(parts[start + 1], out var secs))
        {
            return false;
        }

        var totalSeconds = (parts.Length == 3 ? hours * 3600 : 0) + minutes * 60 + secs;
        if (totalSeconds <= 0)
        {
            return false;
        }

        seconds = totalSeconds;
        return true;
    }

    private static string BuildChapterMarkersText(
        IReadOnlyList<ChapterTopic> topics,
        string transcript,
        double durationSeconds,
        int maxChapters,
        CancellationToken cancellationToken)
    {
        if (topics.Count == 0 || durationSeconds <= 0 || string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        var normalized = NormalizeTranscript(transcript);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var markers = BuildChapterMarkers(topics, normalized, durationSeconds, maxChapters, cancellationToken);
        if (markers.Count == 0)
        {
            return string.Empty;
        }

        var includeHours = durationSeconds >= 3600;
        return string.Join(Environment.NewLine, markers.Select(marker =>
            $"{FormatChapterTimestamp(marker.Start, includeHours)} {marker.Title}"));
    }

    private static string NormalizeTranscript(string transcript)
    {
        var trimmed = transcript?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return Regex.Replace(trimmed, @"\s+", " ");
    }

    private sealed record ChapterMarker(TimeSpan Start, string Title);
    private sealed record ChapterCandidate(ChapterTopic Topic, double Position, int Order);

    private static List<ChapterMarker> BuildChapterMarkers(
        IReadOnlyList<ChapterTopic> topics,
        string transcript,
        double durationSeconds,
        int maxChapters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lowerTranscript = transcript.ToLowerInvariant();
        var normalizedTranscript = BuildNormalizedTranscriptMap(transcript, out var normalizedIndexMap);
        var transcriptLength = transcript.Length;
        if (transcriptLength == 0)
        {
            return new List<ChapterMarker>();
        }

        var positions = new double[topics.Count];
        var hasMatch = new bool[topics.Count];

        for (var i = 0; i < topics.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matchIndex = FindTopicMatchIndex(
                lowerTranscript,
                normalizedTranscript,
                normalizedIndexMap,
                topics[i]);
            if (matchIndex >= 0)
            {
                positions[i] = matchIndex;
                hasMatch[i] = true;
            }
            else
            {
                positions[i] = double.NaN;
            }
        }

        FillMissingPositions(positions, hasMatch, transcriptLength);

        var ordered = topics
            .Select((topic, index) => new ChapterCandidate(topic, positions[index], index))
            .OrderBy(x => x.Position)
            .ThenBy(x => x.Order)
            .ToList();

        ordered = MergeSimilarCandidates(ordered);
        if (maxChapters > 0 && ordered.Count > maxChapters)
        {
            ordered = ReduceCandidatesByUniqueness(ordered, maxChapters);
        }

        var starts = ordered
            .Select(entry => (entry.Position / transcriptLength) * durationSeconds)
            .ToArray();

        if (starts.Length > 0)
        {
            starts[0] = 0;
        }

        var minGapSeconds = 1d;
        for (var i = 1; i < starts.Length; i++)
        {
            if (starts[i] <= starts[i - 1])
            {
                starts[i] = starts[i - 1] + minGapSeconds;
            }
        }

        var minChapterSeconds = GetMinChapterSeconds(durationSeconds, ordered.Count);
        var merged = new List<ChapterMarker>();
        for (var i = 0; i < ordered.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = Math.Clamp(starts[i], 0, durationSeconds);
            var title = ordered[i].Topic.Title;

            if (merged.Count == 0)
            {
                merged.Add(new ChapterMarker(TimeSpan.FromSeconds(start), title));
                continue;
            }

            var last = merged[^1];
            var delta = start - last.Start.TotalSeconds;
            if (delta < minChapterSeconds)
            {
                continue;
            }

            merged.Add(new ChapterMarker(TimeSpan.FromSeconds(start), title));
        }

        var step = 1;
        var rounded = new List<ChapterMarker>();
        foreach (var marker in merged)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seconds = RoundToStep(marker.Start.TotalSeconds, step);
            if (rounded.Count > 0 && seconds <= rounded[^1].Start.TotalSeconds)
            {
                seconds = rounded[^1].Start.TotalSeconds + step;
            }

            if (seconds > durationSeconds)
            {
                break;
            }

            rounded.Add(new ChapterMarker(TimeSpan.FromSeconds(seconds), marker.Title));
        }

        return rounded;
    }

    private static List<ChapterCandidate> MergeSimilarCandidates(List<ChapterCandidate> candidates)
    {
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        var merged = new List<ChapterCandidate> { candidates[0] };

        for (var i = 1; i < candidates.Count; i++)
        {
            var current = candidates[i];
            var previous = merged[^1];
            if (SimilarityScore(previous.Topic, current.Topic) >= 0.78d)
            {
                continue;
            }

            merged.Add(current);
        }

        return merged;
    }

    private static List<ChapterCandidate> ReduceCandidatesByUniqueness(List<ChapterCandidate> candidates, int maxChapters)
    {
        var working = new List<ChapterCandidate>(candidates);
        if (maxChapters <= 0 || working.Count <= maxChapters)
        {
            return working;
        }

        while (working.Count > maxChapters)
        {
            var bestIndex = -1;
            var bestScore = double.MinValue;

            for (var i = 1; i < working.Count; i++)
            {
                var score = 0d;
                if (i > 0)
                {
                    score = Math.Max(score, SimilarityScore(working[i].Topic, working[i - 1].Topic));
                }

                if (i + 1 < working.Count)
                {
                    score = Math.Max(score, SimilarityScore(working[i].Topic, working[i + 1].Topic));
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex <= 0 || bestScore <= 0.01d)
            {
                bestIndex = Math.Max(1, working.Count / 2);
            }

            working.RemoveAt(bestIndex);
        }

        return working;
    }

    private static double SimilarityScore(ChapterTopic a, ChapterTopic b)
    {
        var titleA = TextCleaningUtility.NormalizeForComparison(a.Title ?? string.Empty);
        var titleB = TextCleaningUtility.NormalizeForComparison(b.Title ?? string.Empty);
        var titleSimilarity = 0d;
        if (!string.IsNullOrWhiteSpace(titleA) && !string.IsNullOrWhiteSpace(titleB))
        {
            titleSimilarity = TextCleaningUtility.CalculateSimilarity(titleA, titleB);
        }

        var keywordOverlap = KeywordOverlapRatio(a.Keywords, b.Keywords);
        return Math.Max(titleSimilarity, keywordOverlap);
    }

    private static double KeywordOverlapRatio(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (a is null || b is null || a.Count == 0 || b.Count == 0)
        {
            return 0d;
        }

        var setA = a
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => TextCleaningUtility.NormalizeForComparison(k))
            .Where(k => k.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var setB = b
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => TextCleaningUtility.NormalizeForComparison(k))
            .Where(k => k.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (setA.Count == 0 || setB.Count == 0)
        {
            return 0d;
        }

        var intersection = setA.Intersect(setB, StringComparer.OrdinalIgnoreCase).Count();
        var minCount = Math.Min(setA.Count, setB.Count);
        return minCount > 0 ? (double)intersection / minCount : 0d;
    }

    private static int GetMaxChapters(double durationSeconds)
    {
        if (durationSeconds <= 0)
        {
            return 3;
        }

        var blocks = Math.Ceiling(durationSeconds / 600d);
        var maxChapters = (int)(2 * blocks + 1);
        return Math.Clamp(maxChapters, 3, 18);
    }

    private static int FindTopicMatchIndex(
        string lowerTranscript,
        string normalizedTranscript,
        IReadOnlyList<int> normalizedIndexMap,
        ChapterTopic topic)
    {
        if (!string.IsNullOrWhiteSpace(topic.AnchorText))
        {
            var anchor = topic.AnchorText.Trim().ToLowerInvariant();
            var anchorIdx = FindBestAnchorIndex(lowerTranscript, anchor, topic.Keywords);
            if (anchorIdx >= 0)
            {
                return anchorIdx;
            }

            var normalizedAnchor = TextCleaningUtility.NormalizeForComparison(anchor);
            var normalizedIdx = FindBestAnchorIndexNormalized(
                normalizedTranscript,
                normalizedIndexMap,
                normalizedAnchor,
                lowerTranscript,
                topic.Keywords);
            if (normalizedIdx >= 0)
            {
                return normalizedIdx;
            }
        }

    var keywords = topic.Keywords ?? Array.Empty<string>();
    var keywordIdx = FindMedianIndex(lowerTranscript, normalizedTranscript, normalizedIndexMap, keywords);
    if (keywordIdx >= 0)
    {
        return keywordIdx;
    }

    if (!string.IsNullOrWhiteSpace(topic.Title))
    {
        var titleIdx = FindMedianIndex(
            lowerTranscript,
            normalizedTranscript,
            normalizedIndexMap,
            new[] { topic.Title });
        if (titleIdx >= 0)
        {
            return titleIdx;
        }
        }

        return -1;
    }

    private static int FindMedianIndex(
        string lowerTranscript,
        string normalizedTranscript,
        IReadOnlyList<int> normalizedIndexMap,
        IEnumerable<string> candidates)
    {
        var indices = new List<int>();

        foreach (var raw in candidates)
        {
            var candidate = raw?.Trim();
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length < 3)
            {
                continue;
            }

            var idx = FindFirstMatchIndex(lowerTranscript, normalizedTranscript, normalizedIndexMap, candidate);
            if (idx >= 0)
            {
                indices.Add(idx);
            }
        }

        if (indices.Count == 0)
        {
            return -1;
        }

        indices.Sort();
        return indices[indices.Count / 2];
    }

    private static int FindFirstMatchIndex(
        string lowerTranscript,
        string normalizedTranscript,
        IReadOnlyList<int> normalizedIndexMap,
        string candidate)
    {
        var lower = candidate.ToLowerInvariant();
        var idx = IndexOfFrom(lowerTranscript, lower);
        if (idx >= 0)
        {
            return idx;
        }

        var normalized = TextCleaningUtility.NormalizeForComparison(candidate);
        return IndexOfNormalized(normalizedTranscript, normalizedIndexMap, normalized);
    }

    private static int FindBestAnchorIndex(
        string lowerTranscript,
        string anchorLower,
        IReadOnlyList<string>? keywords)
    {
        var occurrences = FindAllOccurrences(lowerTranscript, anchorLower);
        if (occurrences.Count == 0)
        {
            return -1;
        }

        if (occurrences.Count == 1)
        {
            return occurrences[0];
        }

        if (keywords is null || keywords.Count == 0)
        {
            return occurrences[0];
        }

        var bestIndex = occurrences[0];
        var bestScore = -1;
        foreach (var occurrence in occurrences)
        {
            var score = CountKeywordHits(lowerTranscript, keywords, occurrence, 220);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = occurrence;
            }
        }

        return bestIndex;
    }

    private static int FindBestAnchorIndexNormalized(
        string normalizedTranscript,
        IReadOnlyList<int> normalizedIndexMap,
        string normalizedAnchor,
        string lowerTranscript,
        IReadOnlyList<string>? keywords)
    {
        if (string.IsNullOrWhiteSpace(normalizedAnchor))
        {
            return -1;
        }

        var occurrences = FindAllOccurrences(normalizedTranscript, normalizedAnchor);
        if (occurrences.Count == 0)
        {
            return -1;
        }

        var mapped = occurrences
            .Where(idx => idx >= 0 && idx < normalizedIndexMap.Count)
            .Select(idx => normalizedIndexMap[idx])
            .ToList();

        if (mapped.Count == 0)
        {
            return -1;
        }

        if (keywords is null || keywords.Count == 0)
        {
            return mapped[0];
        }

        var bestIndex = mapped[0];
        var bestScore = -1;
        foreach (var occurrence in mapped)
        {
            var score = CountKeywordHits(lowerTranscript, keywords, occurrence, 220);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = occurrence;
            }
        }

        return bestIndex;
    }

    private static int IndexOfFrom(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
        {
            return -1;
        }

        return haystack.IndexOf(needle, StringComparison.Ordinal);
    }

    private static int IndexOfNormalized(
        string normalizedTranscript,
        IReadOnlyList<int> normalizedIndexMap,
        string normalizedNeedle)
    {
        if (string.IsNullOrWhiteSpace(normalizedNeedle) || normalizedIndexMap.Count == 0)
        {
            return -1;
        }

        var idx = normalizedTranscript.IndexOf(normalizedNeedle, StringComparison.Ordinal);
        if (idx < 0 || idx >= normalizedIndexMap.Count)
        {
            return -1;
        }

        return normalizedIndexMap[idx];
    }

    private static List<int> FindAllOccurrences(string haystack, string needle)
    {
        var results = new List<int>();
        if (string.IsNullOrWhiteSpace(needle))
        {
            return results;
        }

        var index = 0;
        while (index < haystack.Length)
        {
            var found = haystack.IndexOf(needle, index, StringComparison.Ordinal);
            if (found < 0)
            {
                break;
            }

            results.Add(found);
            index = found + needle.Length;
        }

        return results;
    }

    private static int CountKeywordHits(
        string lowerTranscript,
        IReadOnlyList<string> keywords,
        int centerIndex,
        int window)
    {
        if (keywords.Count == 0 || lowerTranscript.Length == 0)
        {
            return 0;
        }

        var start = Math.Max(0, centerIndex - window);
        var end = Math.Min(lowerTranscript.Length, centerIndex + window);
        if (end <= start)
        {
            return 0;
        }

        var slice = lowerTranscript[start..end];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hits = 0;

        foreach (var raw in keywords)
        {
            var keyword = raw?.Trim();
            if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 3)
            {
                continue;
            }

            var lower = keyword.ToLowerInvariant();
            if (!seen.Add(lower))
            {
                continue;
            }

            if (slice.Contains(lower, StringComparison.Ordinal))
            {
                hits++;
            }
        }

        return hits;
    }

    private static double GetMinChapterSeconds(double durationSeconds, int topicCount)
    {
        if (durationSeconds <= 0 || topicCount <= 0)
        {
            return 30d;
        }

        var target = durationSeconds / (topicCount * 3d);
        return Math.Clamp(target, 12d, 45d);
    }


    private static string BuildNormalizedTranscriptMap(string transcript, out List<int> indexMap)
    {
        indexMap = new List<int>(transcript.Length);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(transcript.Length);
        var lastWasSpace = true;
        for (var i = 0; i < transcript.Length; i++)
        {
            var c = transcript[i];
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                indexMap.Add(i);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                indexMap.Add(i);
                lastWasSpace = true;
            }
        }

        if (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length -= 1;
            indexMap.RemoveAt(indexMap.Count - 1);
        }

        return sb.ToString();
    }

    private static void FillMissingPositions(double[] positions, bool[] hasMatch, int transcriptLength)
    {
        if (positions.Length == 0)
        {
            return;
        }

        var matchedIndices = Enumerable.Range(0, positions.Length)
            .Where(i => hasMatch[i])
            .ToList();

        if (matchedIndices.Count == 0)
        {
            var gap = transcriptLength / (double)(positions.Length + 1);
            for (var i = 0; i < positions.Length; i++)
            {
                positions[i] = gap * (i + 1);
            }
            return;
        }

        var firstMatch = matchedIndices[0];
        for (var i = 0; i < firstMatch; i++)
        {
            var gap = positions[firstMatch] / (firstMatch + 1);
            positions[i] = gap * (i + 1);
        }

        for (var m = 0; m < matchedIndices.Count - 1; m++)
        {
            var startIndex = matchedIndices[m];
            var endIndex = matchedIndices[m + 1];
            var missingCount = endIndex - startIndex - 1;
            if (missingCount <= 0)
            {
                continue;
            }

            var gap = (positions[endIndex] - positions[startIndex]) / (missingCount + 1);
            for (var i = 1; i <= missingCount; i++)
            {
                positions[startIndex + i] = positions[startIndex] + gap * i;
            }
        }

        var lastMatch = matchedIndices[^1];
        for (var i = lastMatch + 1; i < positions.Length; i++)
        {
            var gap = (transcriptLength - positions[lastMatch]) / (positions.Length - lastMatch);
            positions[i] = positions[lastMatch] + gap * (i - lastMatch);
        }
    }

    private static double RoundToStep(double value, int stepSeconds)
    {
        if (stepSeconds <= 1)
        {
            return value;
        }

        return Math.Round(value / stepSeconds, MidpointRounding.AwayFromZero) * stepSeconds;
    }

    private static string FormatChapterTimestamp(TimeSpan timestamp, bool includeHours)
    {
        if (includeHours)
        {
            return $"{(int)timestamp.TotalHours}:{timestamp.Minutes:00}:{timestamp.Seconds:00}";
        }

        return $"{timestamp.Minutes:00}:{timestamp.Seconds:00}";
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

        if (UploadView is null)
        {
            return;
        }

        var scrollViewer = UploadView.GetUploadContentScrollViewer();
        var anchor = GetSuggestionAnchor(target);
        var previousOffset = scrollViewer?.VerticalOffset ?? 0;
        var previousTop = anchor is not null && scrollViewer is not null
            ? anchor.TranslatePoint(new Point(0, 0), scrollViewer).Y
            : 0;

        switch (target)
        {
            case SuggestionTarget.Title:
                UploadView.ShowTitleSuggestions(title, suggestions);
                break;
            case SuggestionTarget.Description:
                UploadView.ShowDescriptionSuggestions(title, suggestions);
                break;
            case SuggestionTarget.Tags:
                UploadView.ShowTagsSuggestions(title, suggestions);
                break;
        }

        if (anchor is not null && scrollViewer is not null)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    var newTop = anchor.TranslatePoint(new Point(0, 0), scrollViewer).Y;
                    var delta = newTop - previousTop;
                    if (Math.Abs(delta) > 0.5)
                    {
                        scrollViewer.ScrollToVerticalOffset(previousOffset + delta);
                    }
                }),
                DispatcherPriority.Loaded);
        }
    }

    private void ShowSuggestionLoading(SuggestionTarget target, string title, int expectedCount)
    {
        _activeSuggestionTarget = target;

        if (UploadView is null)
        {
            return;
        }

        var scrollViewer = UploadView.GetUploadContentScrollViewer();
        var anchor = GetSuggestionAnchor(target);
        var previousOffset = scrollViewer?.VerticalOffset ?? 0;
        var previousTop = anchor is not null && scrollViewer is not null
            ? anchor.TranslatePoint(new Point(0, 0), scrollViewer).Y
            : 0;

        switch (target)
        {
            case SuggestionTarget.Title:
                UploadView.ShowTitleSuggestionsLoading(title, expectedCount);
                break;
            case SuggestionTarget.Description:
                UploadView.ShowDescriptionSuggestionsLoading(title, expectedCount);
                break;
            case SuggestionTarget.Tags:
                UploadView.ShowTagsSuggestionsLoading(title, expectedCount);
                break;
        }

        if (anchor is not null && scrollViewer is not null)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    var newTop = anchor.TranslatePoint(new Point(0, 0), scrollViewer).Y;
                    var delta = newTop - previousTop;
                    if (Math.Abs(delta) > 0.5)
                    {
                        scrollViewer.ScrollToVerticalOffset(previousOffset + delta);
                    }
                }),
                DispatcherPriority.Loaded);
        }
    }

    private void CloseSuggestionPopup()
    {
        _activeSuggestionTarget = SuggestionTarget.None;
        UploadView.HideSuggestions();
    }

    private FrameworkElement? GetSuggestionAnchor(SuggestionTarget target)
    {
        return target switch
        {
            SuggestionTarget.Title => UploadView.GetTitleSuggestionAnchor(),
            SuggestionTarget.Description => UploadView.GetDescriptionSuggestionAnchor(),
            SuggestionTarget.Tags => UploadView.GetTagsSuggestionAnchor(),
            _ => null
        };
    }

    private async Task RunSuggestionAsync<T>(
        SuggestionTarget target,
        Button triggerButton,
        int desiredCount,
        string fieldLabel,
        string statusGenerating,
        string statusNoSuggestions,
        string statusCanceled,
        Func<Exception, string> statusError,
        Action logStarted,
        Action logNoSuggestions,
        Action logCanceled,
        Action<Exception> logError,
        Func<CancellationToken, Task<T>> producer,
        Func<T, List<string>> suggestionSelector,
        Action<T, List<string>> handleResults,
        Action? onNoSuggestions = null)
    {
        CloseSuggestionPopup();

        StatusTextBlock.Text = statusGenerating;
        triggerButton.IsEnabled = false;

        var cancellationToken = GetNewLlmCancellationToken();

        if (desiredCount > 1)
        {
            ShowSuggestionLoading(target, fieldLabel, desiredCount);
        }

        logStarted();

        try
        {
            var result = await producer(cancellationToken).ConfigureAwait(true); // Keep UI context
            var suggestions = suggestionSelector(result) ?? new List<string>();

            if (suggestions.Count == 0)
            {
                StatusTextBlock.Text = statusNoSuggestions;
                logNoSuggestions();
                onNoSuggestions?.Invoke();
                CloseSuggestionPopup();
                return;
            }

            handleResults(result, suggestions);
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = statusCanceled;
            logCanceled();
            CloseSuggestionPopup();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = statusError(ex);
            logError(ex);
            CloseSuggestionPopup();
        }
        finally
        {
            triggerButton.IsEnabled = true;
            UpdateLogLinkIndicator();
            if (desiredCount <= 1)
            {
                CloseSuggestionPopup();
            }
        }
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
                batch = await producer().ConfigureAwait(false); // OPTIMIZATION: No UI context needed here
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
        OpenLogWindow();
    }

    private void YouTubeOpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenLogWindow();
    }

    private void OpenLogWindow()
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
        catch (Exception ex)
        {
            // OPTIMIZATION: Added logging
            _logger.Debug($"Failed to update log link indicator: {ex.Message}", MainWindowLogSource);
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

            using var response = await httpClient.GetAsync("https://api.github.com/repos/dabinuss/Dabis-Content-Manager/releases/latest").ConfigureAwait(false); // OPTIMIZATION: No UI context needed
            if (!response.IsSuccessStatusCode)
            {
                // OPTIMIZATION: Switch to UI context for UI updates
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusTextBlock.Text = LocalizationHelper.Get("Status.Update.NotPossible");
                });
                _logger.Warning(
                    LocalizationHelper.Format("Log.Updates.HttpFailure", (int)response.StatusCode),
                    UpdatesLogSource);
                return;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var htmlUrl = root.TryGetProperty("html_url", out var urlProp)
                ? urlProp.GetString()
                : "https://github.com/dabinuss/Dabis-Content-Manager/releases";

            if (string.IsNullOrWhiteSpace(tagName))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusTextBlock.Text = LocalizationHelper.Get("Status.Update.InfoMissing");
                });
                _logger.Warning(LocalizationHelper.Get("Log.Updates.NoTag"), UpdatesLogSource);
                return;
            }

            var latestVersionString = tagName.TrimStart('v', 'V');
            if (!Version.TryParse(latestVersionString, out var latestVersion))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusTextBlock.Text = LocalizationHelper.Format("Status.Update.VersionFormat", tagName);
                });
                _logger.Warning(
                    LocalizationHelper.Format("Log.Updates.VersionParseFailed", tagName),
                    UpdatesLogSource);
                return;
            }

            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
                                ?? new Version(0, 0);

            if (latestVersion <= currentVersion)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusTextBlock.Text = LocalizationHelper.Format("Status.Update.LatestVersion", AppVersion);
                });
                _logger.Info(
                    LocalizationHelper.Format("Log.Updates.NoNewVersion", currentVersion, latestVersion),
                    UpdatesLogSource);
                return;
            }

            // OPTIMIZATION: Switch to UI context for MessageBox
            await Dispatcher.InvokeAsync(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Format("Status.Update.NewVersion", latestVersion);
            });
            
            _logger.Info(
                LocalizationHelper.Format("Log.Updates.NewVersionFound", currentVersion, latestVersion),
                UpdatesLogSource);

            var result = await Dispatcher.InvokeAsync(() => MessageBox.Show(
                this,
                LocalizationHelper.Format("Dialog.Update.Available.Text", currentVersion, latestVersion),
                LocalizationHelper.Get("Dialog.Update.Available.Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information));

            if (result == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(htmlUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(htmlUrl)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    // OPTIMIZATION: Added logging
                    _logger.Debug($"Failed to open browser: {ex.Message}", UpdatesLogSource);
                }
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Format("Status.Update.Failed", ex.Message);
            });
            _logger.Error(
                LocalizationHelper.Format("Log.Updates.CheckFailed", ex.Message),
                UpdatesLogSource,
                ex);
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateLogLinkIndicator();
            });
        }
    }

    private async void CheckUpdatesHyperlink_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync().ConfigureAwait(true); // OPTIMIZATION: Keep UI context
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
            // OPTIMIZATION: Use ConfigureAwait for file I/O
            await File.WriteAllTextAsync(dialog.FileName, transcript, Encoding.UTF8).ConfigureAwait(false);
            
            await Dispatcher.InvokeAsync(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Format("Status.Transcription.ExportSuccess", dialog.FileName);
            });
            
            _logger.Info(LocalizationHelper.Format("Log.Transcription.ExportSuccess", dialog.FileName), TranscriptionLogSource);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Format("Status.Transcription.ExportFailed", ex.Message);
            });
            
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
        if (pageIndex == _currentPageIndex)
        {
            return;
        }

        _currentPageIndex = pageIndex;
        UpdateLastSubMenuSelection(pageIndex);

        // Alle Pages verstecken
        if (PageUpload is not null) PageUpload.Visibility = Visibility.Collapsed;
        if (PageAccounts is not null) PageAccounts.Visibility = Visibility.Collapsed;
        if (PageChannelProfile is not null) PageChannelProfile.Visibility = Visibility.Collapsed;
        if (PagePresets is not null) PagePresets.Visibility = Visibility.Collapsed;
        if (PageHistory is not null) PageHistory.Visibility = Visibility.Collapsed;
        if (PageGeneralSettings is not null) PageGeneralSettings.Visibility = Visibility.Collapsed;
        if (PageLlmSettings is not null) PageLlmSettings.Visibility = Visibility.Collapsed;

        // Gewählte Page anzeigen
        switch (pageIndex)
        {
            case 0:
                if (PageUpload is not null) PageUpload.Visibility = Visibility.Visible;
                break;
            case 1:
                if (PageAccounts is not null) PageAccounts.Visibility = Visibility.Visible;
                break;
            case 6:
                if (PageChannelProfile is not null) PageChannelProfile.Visibility = Visibility.Visible;
                break;
            case 2:
                if (PagePresets is not null) PagePresets.Visibility = Visibility.Visible;
                break;
            case 3:
                if (PageHistory is not null) PageHistory.Visibility = Visibility.Visible;
                break;
            case 4:
                if (PageGeneralSettings is not null) PageGeneralSettings.Visibility = Visibility.Visible;
                break;
            case 5:
                if (PageLlmSettings is not null) PageLlmSettings.Visibility = Visibility.Visible;
                break;
        }

        UpdateMainMenuSelection(pageIndex);
        UpdatePageHeader(pageIndex);
        UpdatePageActions(pageIndex);
    }

    private void UpdatePageHeader(int pageIndex)
    {
        if (PageTitleTextBlock is null || PageContextTextBlock is null)
        {
            return;
        }

        string title;
        string context;
        string breadcrumb;

        switch (pageIndex)
        {
            case 0:
                title = LocalizationHelper.Get("Nav.Uploads");
                context = LocalizationHelper.Get("Page.Context.Upload");
                breadcrumb = LocalizationHelper.Get("Page.Breadcrumb.Uploads");
                break;
            case 1:
                title = LocalizationHelper.Get("Nav.Connections");
                context = LocalizationHelper.Get("Page.Context.Accounts");
                breadcrumb = LocalizationHelper.Get("Page.Breadcrumb.Connections");
                break;
            case 6:
                title = LocalizationHelper.Get("Nav.ChannelProfile");
                context = LocalizationHelper.Get("Page.Context.Channel");
                breadcrumb = LocalizationHelper.Get("Page.Breadcrumb.Connections");
                break;
            case 2:
                title = LocalizationHelper.Get("Nav.Presets");
                context = LocalizationHelper.Get("Page.Context.Presets");
                breadcrumb = LocalizationHelper.Get("Page.Breadcrumb.Uploads");
                break;
            case 3:
                title = LocalizationHelper.Get("Nav.History");
                context = LocalizationHelper.Get("Page.Context.History");
                breadcrumb = LocalizationHelper.Get("Page.Breadcrumb.Uploads");
                break;
            case 4:
                title = LocalizationHelper.Get("Nav.Settings");
                context = LocalizationHelper.Get("Page.Context.Settings");
                breadcrumb = LocalizationHelper.Get("Page.Breadcrumb.Settings");
                break;
            case 5:
                title = LocalizationHelper.Get("Settings.LLM");
                context = LocalizationHelper.Get("Page.Context.LlmSettings");
                breadcrumb = LocalizationHelper.Get("Page.Breadcrumb.Settings");
                break;
            default:
                title = LocalizationHelper.Get("App.Name");
                context = string.Empty;
                breadcrumb = LocalizationHelper.Get("Page.Breadcrumb.Default");
                break;
        }

        PageTitleTextBlock.Text = title;
        PageContextTextBlock.Text = context;

        if (PageBreadcrumbTextBlock is not null)
        {
            PageBreadcrumbTextBlock.Text = breadcrumb;
        }
    }

    private void UpdatePageActions(int pageIndex)
    {
        if (PageActionsUpload is null ||
            PageActionsAccounts is null ||
            PageActionsChannelProfile is null ||
            PageActionsPresets is null ||
            PageActionsHistory is null ||
            PageActionsGeneralSettings is null ||
            PageActionsLlmSettings is null)
        {
            return;
        }

        PageActionsUpload.Visibility = Visibility.Collapsed;
        PageActionsAccounts.Visibility = Visibility.Collapsed;
        PageActionsChannelProfile.Visibility = Visibility.Collapsed;
        PageActionsPresets.Visibility = Visibility.Collapsed;
        PageActionsHistory.Visibility = Visibility.Collapsed;
        PageActionsGeneralSettings.Visibility = Visibility.Collapsed;
        PageActionsLlmSettings.Visibility = Visibility.Collapsed;

        switch (pageIndex)
        {
            case 0:
                PageActionsUpload.Visibility = Visibility.Visible;
                break;
            case 1:
                PageActionsAccounts.Visibility = Visibility.Visible;
                break;
            case 6:
                PageActionsChannelProfile.Visibility = Visibility.Visible;
                break;
            case 2:
                PageActionsPresets.Visibility = Visibility.Visible;
                break;
            case 3:
                PageActionsHistory.Visibility = Visibility.Visible;
                break;
            case 4:
                PageActionsGeneralSettings.Visibility = Visibility.Visible;
                break;
            case 5:
                PageActionsLlmSettings.Visibility = Visibility.Visible;
                break;
        }
    }

    private void YouTubeServiceIcon_Click(object sender, MouseButtonEventArgs e)
    {
        // Navigiere zum Connections-Tab
        NavConnections.IsChecked = true;
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
        catch (Exception ex)
        {
            // OPTIMIZATION: Added logging
            _logger.Debug($"Failed to open URL in browser: {ex.Message}", MainWindowLogSource);
        }

        e.Handled = true;
    }

    #endregion

    #region Top Navigation

    private void MainMenu_Checked(object sender, RoutedEventArgs e)
    {
        if (UploadsSubMenuPanel is null ||
            ConnectionsSubMenuPanel is null ||
            SettingsSubMenuPanel is null)
        {
            return;
        }

        if (sender == MainNavUploads)
        {
            SetSubMenuVisibility(UploadsSubMenuPanel);
            var target = NavUpload;
            if (target is not null && target.IsChecked != true)
            {
                target.IsChecked = true;
            }
            if (target?.Tag is string tagString && int.TryParse(tagString, out var pageIndex))
            {
                ShowPage(pageIndex);
            }
        }
        else if (sender == MainNavConnections)
        {
            SetSubMenuVisibility(ConnectionsSubMenuPanel);
            var target = GetConnectionsSubMenuButton(_lastConnectionsPageIndex) ?? NavConnections;
            if (target is not null && target.IsChecked != true)
            {
                target.IsChecked = true;
            }
            if (target?.Tag is string tagString && int.TryParse(tagString, out var pageIndex))
            {
                ShowPage(pageIndex);
            }
        }
        else if (sender == MainNavSettings)
        {
            SetSubMenuVisibility(SettingsSubMenuPanel);
            var target = NavSettingsGeneral;
            if (target is not null && target.IsChecked != true)
            {
                target.IsChecked = true;
            }
            if (target?.Tag is string tagString && int.TryParse(tagString, out var pageIndex))
            {
                ShowPage(pageIndex);
            }
        }
    }

    private void SetSubMenuVisibility(UIElement activePanel)
    {
        UploadsSubMenuPanel.Visibility = activePanel == UploadsSubMenuPanel
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConnectionsSubMenuPanel.Visibility = activePanel == ConnectionsSubMenuPanel
            ? Visibility.Visible
            : Visibility.Collapsed;
        SettingsSubMenuPanel.Visibility = activePanel == SettingsSubMenuPanel
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void EnsureSubMenuChecked(Panel panel, RadioButton? defaultButton)
    {
        if (defaultButton is null)
        {
            return;
        }

        foreach (var child in panel.Children)
        {
            if (child is RadioButton radioButton && radioButton.IsChecked == true)
            {
                return;
            }
        }

        defaultButton.IsChecked = true;
    }

    private void UpdateMainMenuSelection(int pageIndex)
    {
        if (MainNavUploads is null || MainNavConnections is null || MainNavSettings is null)
        {
            return;
        }

        switch (pageIndex)
        {
            case 0:
            case 3:
            case 2:
                if (MainNavUploads.IsChecked != true)
                {
                    MainNavUploads.IsChecked = true;
                }
                break;
            case 1:
            case 6:
                if (MainNavConnections.IsChecked != true)
                {
                    MainNavConnections.IsChecked = true;
                }
                break;
            case 4:
            case 5:
                if (MainNavSettings.IsChecked != true)
                {
                    MainNavSettings.IsChecked = true;
                }
                break;
        }
    }

    private void UpdateLastSubMenuSelection(int pageIndex)
    {
        switch (pageIndex)
        {
            case 0:
            case 2:
            case 3:
                _lastUploadsPageIndex = pageIndex;
                break;
            case 1:
            case 6:
                _lastConnectionsPageIndex = pageIndex;
                break;
            case 4:
            case 5:
                _lastSettingsPageIndex = pageIndex;
                break;
        }
    }

    private RadioButton? GetUploadsSubMenuButton(int pageIndex) => pageIndex switch
    {
        0 => NavUpload,
        2 => NavPresets,
        3 => NavHistory,
        _ => NavUpload
    };

    private RadioButton? GetConnectionsSubMenuButton(int pageIndex) => pageIndex switch
    {
        6 => NavChannelProfile,
        _ => NavConnections
    };

    private RadioButton? GetSettingsSubMenuButton(int pageIndex) => pageIndex switch
    {
        5 => NavSettingsLlm,
        _ => NavSettingsGeneral
    };

    #endregion

    private enum SuggestionTarget
    {
        None,
        Title,
        Description,
        Tags
    }

    private sealed class PresetApplicationState
    {
        public UploadPreset? Preset { get; private set; }
        public bool HasDescriptionPlaceholder { get; private set; }
        public string? BaseDescription { get; private set; }
        public string? LastResult { get; private set; }

        public void Record(UploadPreset preset, string baseDescription, string result, bool hasPlaceholder)
        {
            Preset = preset;
            BaseDescription = baseDescription;
            LastResult = result ?? string.Empty;
            HasDescriptionPlaceholder = hasPlaceholder;
        }

        public void Reset()
        {
            Preset = null;
            BaseDescription = null;
            LastResult = null;
            HasDescriptionPlaceholder = false;
        }

        public void ClearPreset()
        {
            Preset = null;
            HasDescriptionPlaceholder = false;
        }

        public bool Matches(UploadPreset? preset) =>
            Preset is not null &&
            preset is not null &&
            string.Equals(Preset.Id, preset.Id, StringComparison.OrdinalIgnoreCase);

        public bool TryGetBaseDescription(UploadPreset preset, out string baseDescription)
        {
            if (Matches(preset) && BaseDescription is not null)
            {
                baseDescription = BaseDescription;
                return true;
            }

            baseDescription = string.Empty;
            return false;
        }

        public bool TryGetBaseDescription(out string baseDescription)
        {
            if (!string.IsNullOrWhiteSpace(BaseDescription))
            {
                baseDescription = BaseDescription;
                return true;
            }

            baseDescription = string.Empty;
            return false;
        }

        public bool IsLastResult(string? currentDescription)
        {
            if (string.IsNullOrWhiteSpace(LastResult))
            {
                return false;
            }

            var trimmedCurrent = (currentDescription ?? string.Empty).Trim();
            var trimmedResult = (LastResult ?? string.Empty).Trim();
            return string.Equals(trimmedCurrent, trimmedResult, StringComparison.Ordinal);
        }

        public void UpdateBaseDescriptionIfChanged(string? currentDescription)
        {
            var trimmedCurrent = (currentDescription ?? string.Empty).Trim();
            var trimmedResult = (LastResult ?? string.Empty).Trim();
            if (Preset is null || !string.Equals(trimmedCurrent, trimmedResult, StringComparison.Ordinal))
            {
                BaseDescription = currentDescription;
            }
        }

        public void UpdateLastResult(string result)
        {
            if (Preset is null)
            {
                return;
            }

            LastResult = result ?? string.Empty;
        }
    }
}
