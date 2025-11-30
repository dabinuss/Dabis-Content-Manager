using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DCM.Core.Configuration;
using DCM.Core.Models;
using DCM.Core.Services;
using DCM.YouTube;

namespace DCM.App;

public partial class MainWindow : Window
{
    private readonly TemplateService _templateService = new();
    private readonly ITemplateRepository _templateRepository;
    private readonly ISettingsProvider _settingsProvider;
    private readonly YouTubePlatformClient _youTubeClient;
    private UploadService _uploadService;
    private AppSettings _settings = new();

    private Template[] _loadedTemplates = Array.Empty<Template>();

    private readonly System.Collections.Generic.List<YouTubePlaylistListItem> _youTubePlaylists = new();

    private sealed class YouTubePlaylistListItem
    {
        public YouTubePlaylistListItem(string id, string title)
        {
            Id = id;
            Title = title;
        }

        public string Id { get; }

        public string Title { get; }

        public override string ToString() => Title;
    }

    public MainWindow()
    {
        InitializeComponent();

        // JSON-Konfigsystem initialisieren
        _settingsProvider = new JsonSettingsProvider();
        _templateRepository = new JsonTemplateRepository();
        _youTubeClient = new YouTubePlatformClient();
        _uploadService = new UploadService(new IPlatformClient[] { _youTubeClient }, _templateService);

        LoadSettings();
        InitializePlatformComboBox();
        InitializeSchedulingDefaults();
        LoadTemplates();
        UpdateYouTubeStatusText(); // Anfangsstatus

        // Auto-Reconnect nach dem Laden des Fensters
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await TryAutoConnectYouTubeAsync();
    }

    #region Initial Load

    private void LoadSettings()
    {
        try
        {
            _settings = _settingsProvider.Load();
        }
        catch (Exception ex)
        {
            _settings = new AppSettings();
            StatusTextBlock.Text = $"Einstellungen konnten nicht geladen werden: {ex.Message}";
        }
    }

    private void InitializePlatformComboBox()
    {
        PlatformComboBox.ItemsSource = Enum.GetValues(typeof(PlatformType));
        PlatformComboBox.SelectedItem = _settings.DefaultPlatform;
    }

    private void InitializeSchedulingDefaults()
    {
        // Default: nächster Tag 18:00 Uhr
        ScheduleDatePicker.SelectedDate = DateTime.Today.AddDays(1);
        ScheduleTimeTextBox.Text = "18:00";
        UpdateScheduleControlsEnabled();
    }

    private void LoadTemplates()
    {
        try
        {
            _loadedTemplates = _templateRepository.Load().ToArray();

            // Templates-Tab
            TemplateListBox.ItemsSource = _loadedTemplates;
            TemplateListBox.SelectionChanged += TemplateListBox_SelectionChanged;

            var defaultTemplate = _loadedTemplates.FirstOrDefault(t => t.IsDefault && t.Platform == PlatformType.YouTube)
                                  ?? _loadedTemplates.FirstOrDefault(t => t.Platform == PlatformType.YouTube);

            if (defaultTemplate is not null)
            {
                TemplateListBox.SelectedItem = defaultTemplate;
                TemplateBodyTextBox.Text = defaultTemplate.Body;
            }

            // Template-Auswahl im Upload-Tab
            TemplateComboBox.ItemsSource = _loadedTemplates;
            if (defaultTemplate is not null)
            {
                TemplateComboBox.SelectedItem = defaultTemplate;
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Templates konnten nicht geladen werden: {ex.Message}";
        }
    }

    #endregion

    #region Events – Templates & Upload

    private void TemplateListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateListBox.SelectedItem is Template tmpl)
        {
            TemplateBodyTextBox.Text = tmpl.Body;
        }
        else
        {
            TemplateBodyTextBox.Text = string.Empty;
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Videodatei auswählen",
            Filter = "Video-Dateien|*.mp4;*.mkv;*.mov;*.avi;*.webm|Alle Dateien|*.*",
            InitialDirectory = !string.IsNullOrWhiteSpace(_settings.LastVideoFolder) && Directory.Exists(_settings.LastVideoFolder)
                ? _settings.LastVideoFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (dlg.ShowDialog(this) == true)
        {
            VideoPathTextBox.Text = dlg.FileName;
            _settings.LastVideoFolder = Path.GetDirectoryName(dlg.FileName);
            SaveSettings();
        }
    }

    private void GenerateTitleButton_Click(object sender, RoutedEventArgs e)
    {
        var path = VideoPathTextBox.Text;

        if (string.IsNullOrWhiteSpace(path))
        {
            TitleTextBox.Text = "Neues Video";
            return;
        }

        var fileName = Path.GetFileNameWithoutExtension(path);
        TitleTextBox.Text = string.IsNullOrWhiteSpace(fileName) ? "Neues Video" : fileName;
    }

    private void ApplyTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        Template? tmpl = TemplateComboBox.SelectedItem as Template;

        // Fallback: Auswahl im Templates-Tab
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
        var project = BuildUploadProjectFromUi(includeScheduling: true);

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

        try
        {
            Template? selectedTemplate = TemplateComboBox.SelectedItem as Template;

            var result = await _uploadService.UploadAsync(project, selectedTemplate, CancellationToken.None);

            if (result.Success)
            {
                StatusTextBlock.Text = $"Upload erfolgreich: {result.VideoUrl}";
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
    }

    #endregion

    #region Events – YouTube Konto & Playlists

    private async void YouTubeConnectButton_Click(object sender, RoutedEventArgs e)
    {
        StatusTextBlock.Text = "Verbinde mit YouTube...";

        try
        {
            await _youTubeClient.ConnectAsync(CancellationToken.None);
            UpdateYouTubeStatusText();
            await RefreshYouTubePlaylistsAsync();
            StatusTextBlock.Text = "Mit YouTube verbunden.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"YouTube-Verbindung fehlgeschlagen: {ex.Message}";
        }
    }

    private async void YouTubeDisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        StatusTextBlock.Text = "YouTube-Verbindung wird getrennt...";

        try
        {
            await _youTubeClient.DisconnectAsync();
            UpdateYouTubeStatusText();
            _youTubePlaylists.Clear();
            YouTubePlaylistsListBox.ItemsSource = null;
            PlaylistComboBox.ItemsSource = null;
            StatusTextBlock.Text = "YouTube-Verbindung getrennt.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Trennen fehlgeschlagen: {ex.Message}";
        }
    }

    private void YouTubePlaylistsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (YouTubePlaylistsListBox.SelectedItem is YouTubePlaylistListItem item)
        {
            _settings.DefaultPlaylistId = item.Id;
            SaveSettings();
            StatusTextBlock.Text = $"Standard-Playlist gesetzt: {item.Title}";

            // Auswahl im Upload-Tab (PlaylistComboBox) synchronisieren
            if (PlaylistComboBox.ItemsSource is not null)
            {
                var selected = _youTubePlaylists.FirstOrDefault(p => p.Id == item.Id);
                if (selected is not null)
                {
                    PlaylistComboBox.SelectedItem = selected;
                }
            }
        }
    }

    #endregion

    #region Helpers

    private UploadProject BuildUploadProjectFromUi(bool includeScheduling)
    {
        // Plattform
        var platform = PlatformType.YouTube;
        if (PlatformComboBox.SelectedItem is PlatformType selectedPlatform)
        {
            platform = selectedPlatform;
        }
        else
        {
            platform = _settings.DefaultPlatform;
        }

        // Sichtbarkeit
        var visibility = VideoVisibility.Unlisted;
        if (VisibilityComboBox.SelectedItem is ComboBoxItem visItem && visItem.Tag is VideoVisibility visEnum)
        {
            visibility = visEnum;
        }

        // Playlist (Upload-Tab-Auswahl bevorzugen)
        string? playlistId = null;
        if (PlaylistComboBox.SelectedItem is YouTubePlaylistListItem plItem)
        {
            playlistId = plItem.Id;
        }
        else
        {
            playlistId = _settings.DefaultPlaylistId;
        }

        // Scheduling
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
                    // Fallback: 18:00 Uhr
                    timeOfDay = new TimeSpan(18, 0, 0);
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
            ScheduledTime = scheduledTime
        };

        // Tags kommen später über eigene UI.
        return project;
    }

    private void SaveSettings()
    {
        try
        {
            _settingsProvider.Save(_settings);
        }
        catch
        {
            // Für jetzt nicht kritisch.
        }
    }

    private void UpdateYouTubeStatusText()
    {
        if (_youTubeClient.IsConnected)
        {
            YouTubeAccountStatusTextBlock.Text = string.IsNullOrWhiteSpace(_youTubeClient.ChannelTitle)
                ? "Mit YouTube verbunden."
                : $"Verbunden als: {_youTubeClient.ChannelTitle}";
        }
        else
        {
            YouTubeAccountStatusTextBlock.Text = "Nicht mit YouTube verbunden.";
        }
    }

    private async System.Threading.Tasks.Task RefreshYouTubePlaylistsAsync()
    {
        if (!_youTubeClient.IsConnected)
        {
            _youTubePlaylists.Clear();
            YouTubePlaylistsListBox.ItemsSource = null;
            PlaylistComboBox.ItemsSource = null;
            return;
        }

        try
        {
            var playlists = await _youTubeClient.GetPlaylistsAsync(CancellationToken.None);

            _youTubePlaylists.Clear();
            _youTubePlaylists.AddRange(
                playlists.Select(p => new YouTubePlaylistListItem(p.Id, p.Title)));

            YouTubePlaylistsListBox.ItemsSource = _youTubePlaylists;
            PlaylistComboBox.ItemsSource = _youTubePlaylists;

            if (!string.IsNullOrWhiteSpace(_settings.DefaultPlaylistId))
            {
                var selected = _youTubePlaylists.FirstOrDefault(i => i.Id == _settings.DefaultPlaylistId);
                if (selected is not null)
                {
                    YouTubePlaylistsListBox.SelectedItem = selected;
                    PlaylistComboBox.SelectedItem = selected;
                }
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler beim Laden der Playlists: {ex.Message}";
        }
    }

    private void ScheduleCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        UpdateScheduleControlsEnabled();
    }

    private void UpdateScheduleControlsEnabled()
    {
        var enabled = ScheduleCheckBox.IsChecked == true;
        ScheduleDatePicker.IsEnabled = enabled;
        ScheduleTimeTextBox.IsEnabled = enabled;
    }

    /// <summary>
    /// Versucht beim Programmstart automatisch mit YouTube zu verbinden,
    /// falls bereits Tokens vorhanden sind. Es wird dabei garantiert
    /// kein Browser geöffnet.
    /// </summary>
    private async System.Threading.Tasks.Task TryAutoConnectYouTubeAsync()
    {
        try
        {
            var connected = await _youTubeClient.TryConnectSilentAsync(CancellationToken.None);
            if (!connected)
            {
                // Kein Auto-Login möglich (z.B. keine Tokens) → still bleiben.
                return;
            }

            UpdateYouTubeStatusText();
            await RefreshYouTubePlaylistsAsync();
            StatusTextBlock.Text = "YouTube-Verbindung wiederhergestellt.";
        }
        catch
        {
            // Auto-Login ist nur Komfort – bei Fehler kann der User normal verbinden.
        }
    }

    #endregion
}
