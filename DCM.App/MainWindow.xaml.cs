using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Core.Models;
using DCM.Core.Services;
using DCM.Llm;
using DCM.YouTube;

namespace DCM.App;

public partial class MainWindow : Window
{
    private readonly TemplateService _templateService = new();
    private readonly ITemplateRepository _templateRepository;
    private readonly ISettingsProvider _settingsProvider;
    private readonly YouTubePlatformClient _youTubeClient;
    private readonly UploadHistoryService _uploadHistoryService;
    private readonly SimpleFallbackSuggestionService _fallbackSuggestionService;

    private ILlmClient _llmClient;
    private IContentSuggestionService _contentSuggestionService;
    private UploadService _uploadService;
    private AppSettings _settings = new();

    private readonly List<Template> _loadedTemplates = new();
    private Template? _currentEditingTemplate;

    private readonly List<YouTubePlaylistInfo> _youTubePlaylists = new();

    private List<UploadHistoryEntry> _allHistoryEntries = new();

    private CancellationTokenSource? _currentLlmCts;

    public MainWindow()
    {
        InitializeComponent();

        _settingsProvider = new JsonSettingsProvider();
        _templateRepository = new JsonTemplateRepository();
        _youTubeClient = new YouTubePlatformClient();
        _uploadHistoryService = new UploadHistoryService();
        _fallbackSuggestionService = new SimpleFallbackSuggestionService();

        LoadSettings();

        _llmClient = CreateLlmClient(_settings.Llm);
        _contentSuggestionService = new ContentSuggestionService(
            _llmClient,
            _fallbackSuggestionService,
            _settings.Llm);

        _uploadService = new UploadService(new IPlatformClient[] { _youTubeClient }, _templateService);

        InitializePlatformComboBox();
        InitializeLanguageComboBox();
        InitializeSchedulingDefaults();
        InitializeLlmSettingsTab();
        LoadTemplates();
        UpdateYouTubeStatusText();
        LoadUploadHistory();

        LlmTemperatureSlider.ValueChanged += LlmTemperatureSlider_ValueChanged;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    #region Lifecycle

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
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
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_settings.AutoConnectYouTube)
        {
            await TryAutoConnectYouTubeAsync();
        }

        UpdateLlmStatusText();
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

    private static ILlmClient CreateLlmClient(LlmSettings settings)
    {
        if (settings.IsLocalMode && !string.IsNullOrWhiteSpace(settings.LocalModelPath))
        {
            return new LocalLlmClient(settings);
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
            _settings.Llm);

        UpdateLlmStatusText();
    }

    #endregion

    #region Initialization

    private void InitializePlatformComboBox()
    {
        PlatformComboBox.ItemsSource = Enum.GetValues(typeof(PlatformType));
        PlatformComboBox.SelectedItem = _settings.DefaultPlatform;

        TemplatePlatformComboBox.ItemsSource = Enum.GetValues(typeof(PlatformType));

        SelectComboBoxItemByTag(VisibilityComboBox, _settings.DefaultVisibility);
    }

    private void InitializeLanguageComboBox()
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

    #region Upload Events

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        string initialDirectory;

        if (!string.IsNullOrWhiteSpace(_settings.DefaultVideoFolder) &&
            Directory.Exists(_settings.DefaultVideoFolder))
        {
            initialDirectory = _settings.DefaultVideoFolder;
        }
        else if (!string.IsNullOrWhiteSpace(_settings.LastVideoFolder) &&
                 Directory.Exists(_settings.LastVideoFolder))
        {
            initialDirectory = _settings.LastVideoFolder;
        }
        else
        {
            initialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        }

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Videodatei auswählen",
            Filter = "Video-Dateien|*.mp4;*.mkv;*.mov;*.avi;*.webm|Alle Dateien|*.*",
            InitialDirectory = initialDirectory
        };

        if (dlg.ShowDialog(this) == true)
        {
            VideoPathTextBox.Text = dlg.FileName;
            _settings.LastVideoFolder = Path.GetDirectoryName(dlg.FileName);
            SaveSettings();
        }
    }

    private void ThumbnailBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Thumbnail-Datei auswählen",
            Filter = "Bilddateien|*.png;*.jpg;*.jpeg|Alle Dateien|*.*"
        };

        string initialDirectory;

        if (!string.IsNullOrWhiteSpace(_settings.DefaultThumbnailFolder) &&
            Directory.Exists(_settings.DefaultThumbnailFolder))
        {
            initialDirectory = _settings.DefaultThumbnailFolder;
        }
        else if (!string.IsNullOrWhiteSpace(_settings.LastVideoFolder) &&
                 Directory.Exists(_settings.LastVideoFolder))
        {
            initialDirectory = _settings.LastVideoFolder;
        }
        else
        {
            initialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        }

        dlg.InitialDirectory = initialDirectory;

        if (dlg.ShowDialog(this) == true)
        {
            ThumbnailPathTextBox.Text = dlg.FileName;
            UpdateThumbnailPreview(dlg.FileName);
        }
    }

    private void ThumbnailClearButton_Click(object sender, RoutedEventArgs e)
    {
        ThumbnailPathTextBox.Text = string.Empty;
        UpdateThumbnailPreview(null);
    }

    private void UpdateThumbnailPreview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ThumbnailPreviewBorder.Visibility = Visibility.Collapsed;
            ThumbnailPreviewImage.Source = null;
            return;
        }

        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.DecodePixelHeight = 54;
            bitmap.EndInit();
            bitmap.Freeze();

            ThumbnailPreviewImage.Source = bitmap;
            ThumbnailPreviewBorder.Visibility = Visibility.Visible;
        }
        catch
        {
            ThumbnailPreviewBorder.Visibility = Visibility.Collapsed;
            ThumbnailPreviewImage.Source = null;
        }
    }

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
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.ConfirmBeforeUpload)
        {
            var confirmResult = System.Windows.MessageBox.Show(
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
            project.ThumbnailPath = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(project.ThumbnailPath))
        {
            var fileInfo = new FileInfo(project.ThumbnailPath);
            if (fileInfo.Length > Constants.MaxThumbnailSizeBytes)
            {
                StatusTextBlock.Text = "Thumbnail ist größer als 2 MB und wird nicht verwendet.";
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
            return;
        }

        if (project.Platform == PlatformType.YouTube && !_youTubeClient.IsConnected)
        {
            StatusTextBlock.Text = "Bitte zuerst im Tab \"Konten\" mit YouTube verbinden.";
            return;
        }

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
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Unerwarteter Fehler beim Upload: {ex.Message}";
        }
        finally
        {
            HideUploadProgress();
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
                return;
            }

            TitleTextBox.Text = titles[0];
            StatusTextBlock.Text = $"Titelvorschlag eingefügt. ({titles.Count} Vorschläge)";
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Titelgenerierung abgebrochen.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler bei Titelgenerierung: {ex.Message}";
        }
        finally
        {
            GenerateTitleButton.IsEnabled = true;
        }
    }

    private async void GenerateDescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();

        StatusTextBlock.Text = "Generiere Beschreibungsvorschlag...";
        GenerateDescriptionButton.IsEnabled = false;

        var cancellationToken = GetNewLlmCancellationToken();

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
            }
            else
            {
                StatusTextBlock.Text = "Keine Beschreibungsvorschläge verfügbar.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Beschreibungsgenerierung abgebrochen.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler bei Beschreibungsgenerierung: {ex.Message}";
        }
        finally
        {
            GenerateDescriptionButton.IsEnabled = true;
        }
    }

    private async void GenerateTagsButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);
        _settings.Persona ??= new ChannelPersona();

        StatusTextBlock.Text = "Generiere Tag-Vorschläge...";
        GenerateTagsButton.IsEnabled = false;

        var cancellationToken = GetNewLlmCancellationToken();

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
            }
            else
            {
                StatusTextBlock.Text = "Keine Tag-Vorschläge verfügbar.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Tag-Generierung abgebrochen.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler bei Tag-Generierung: {ex.Message}";
        }
        finally
        {
            GenerateTagsButton.IsEnabled = true;
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
        var confirm = System.Windows.MessageBox.Show(
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
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Historie konnte nicht gelöscht werden: {ex.Message}";
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

    private void HistoryLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
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

    #region Helpers

    private UploadProject BuildUploadProjectFromUi(bool includeScheduling)
    {
        var platform = PlatformType.YouTube;
        if (PlatformComboBox.SelectedItem is PlatformType selectedPlatform)
        {
            platform = selectedPlatform;
        }
        else
        {
            platform = _settings.DefaultPlatform;
        }

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
}