using System;
using System.Collections.Generic;
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
    private const long MaxThumbnailSizeBytes = 1 * 1024 * 1024; // 1 MB

    private readonly TemplateService _templateService = new();
    private readonly ITemplateRepository _templateRepository;
    private readonly ISettingsProvider _settingsProvider;
    private readonly YouTubePlatformClient _youTubeClient;
    private UploadService _uploadService;
    private AppSettings _settings = new();

    private readonly List<Template> _loadedTemplates = new();
    private Template? _currentEditingTemplate;

    private readonly List<YouTubePlaylistListItem> _youTubePlaylists = new();

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
        // Upload-Tab
        PlatformComboBox.ItemsSource = Enum.GetValues(typeof(PlatformType));
        PlatformComboBox.SelectedItem = _settings.DefaultPlatform;

        // Templates-Tab
        TemplatePlatformComboBox.ItemsSource = Enum.GetValues(typeof(PlatformType));
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
            _loadedTemplates.Clear();
            _loadedTemplates.AddRange(_templateRepository.Load());

            TemplateListBox.ItemsSource = _loadedTemplates;

            var defaultTemplate = _loadedTemplates.FirstOrDefault(t => t.IsDefault && t.Platform == PlatformType.YouTube)
                                  ?? _loadedTemplates.FirstOrDefault(t => t.Platform == PlatformType.YouTube);

            if (defaultTemplate is not null)
            {
                TemplateListBox.SelectedItem = defaultTemplate;
                LoadTemplateIntoEditor(defaultTemplate);
            }
            else
            {
                LoadTemplateIntoEditor(null);
            }

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
            LoadTemplateIntoEditor(tmpl);
        }
        else
        {
            LoadTemplateIntoEditor(null);
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

    private void ThumbnailBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
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
            // DefaultThumbnailFolder bleibt vorerst konfigurierbar für spätere Phasen.
        }
    }

    private void ThumbnailClearButton_Click(object sender, RoutedEventArgs e)
    {
        ThumbnailPathTextBox.Text = string.Empty;
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

        // Milder Check für Thumbnail – nur Hinweis, kein Abbruch.
        if (!string.IsNullOrWhiteSpace(project.ThumbnailPath) &&
            !File.Exists(project.ThumbnailPath))
        {
            StatusTextBlock.Text = "Hinweis: Thumbnail-Datei wurde nicht gefunden. Upload wird ohne Thumbnail fortgesetzt.";
            project.ThumbnailPath = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(project.ThumbnailPath))
        {
            // 1-MB-Größenlimit
            var fileInfo = new FileInfo(project.ThumbnailPath);
            if (fileInfo.Length > MaxThumbnailSizeBytes)
            {
                StatusTextBlock.Text = "Thumbnail ist größer als 1 MB und wird nicht verwendet.";
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

    #region Events – Template-CRUD

    private void TemplateNewButton_Click(object sender, RoutedEventArgs e)
    {
        // Neues Template direkt in die Liste aufnehmen
        var tmpl = new Template
        {
            Name = "Neues Template",
            Platform = PlatformType.YouTube,
            // Falls es noch keinen Default für YouTube gibt, dieses als Default markieren
            IsDefault = !_loadedTemplates.Any(t => t.Platform == PlatformType.YouTube && t.IsDefault),
            Body = string.Empty
        };

        _loadedTemplates.Add(tmpl);
        RefreshTemplateBindings();

        TemplateListBox.SelectedItem = tmpl;
        LoadTemplateIntoEditor(tmpl);

        StatusTextBlock.Text = "Neues Template erstellt. Bitte bearbeiten und speichern.";
    }

    private void TemplateEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateListBox.SelectedItem is Template tmpl)
        {
            LoadTemplateIntoEditor(tmpl);
            StatusTextBlock.Text = $"Template \"{tmpl.Name}\" wird bearbeitet.";
        }
        else
        {
            StatusTextBlock.Text = "Kein Template zum Bearbeiten ausgewählt.";
        }
    }

    private void TemplateDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateListBox.SelectedItem is not Template tmpl)
        {
            StatusTextBlock.Text = "Kein Template zum Löschen ausgewählt.";
            return;
        }

        if (_loadedTemplates.Remove(tmpl))
        {
            if (_currentEditingTemplate?.Id == tmpl.Id)
            {
                _currentEditingTemplate = null;
                LoadTemplateIntoEditor(null);
            }

            SaveTemplatesToRepository();
            RefreshTemplateBindings();
            StatusTextBlock.Text = $"Template \"{tmpl.Name}\" gelöscht.";
        }
    }

    private void TemplateSaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentEditingTemplate is null)
            {
                StatusTextBlock.Text = "Kein Template im Editor zum Speichern.";
                return;
            }

            var name = (TemplateNameTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                StatusTextBlock.Text = "Templatename darf nicht leer sein.";
                return;
            }

            var platform = PlatformType.YouTube;
            if (TemplatePlatformComboBox.SelectedItem is PlatformType selectedPlatform)
            {
                platform = selectedPlatform;
            }

            var isDefault = TemplateIsDefaultCheckBox.IsChecked == true;
            var description = TemplateDescriptionTextBox.Text;
            var body = TemplateBodyEditorTextBox.Text ?? string.Empty;

            // Optional: Warnung bei doppelten Namen pro Plattform
            var duplicate = _loadedTemplates
                .FirstOrDefault(t =>
                    t.Platform == platform &&
                    !string.Equals(t.Id, _currentEditingTemplate.Id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

            if (duplicate is not null)
            {
                StatusTextBlock.Text =
                    $"Hinweis: Es existiert bereits ein Template mit diesem Namen für Plattform {platform}.";
                // Wir speichern trotzdem, nur Hinweis.
            }

            _currentEditingTemplate.Name = name;
            _currentEditingTemplate.Platform = platform;
            _currentEditingTemplate.IsDefault = isDefault;
            _currentEditingTemplate.Description = string.IsNullOrWhiteSpace(description) ? null : description;
            _currentEditingTemplate.Body = body;

            // Nur ein Default-Template pro Plattform erzwingen
            if (isDefault)
            {
                foreach (var other in _loadedTemplates.Where(t =>
                         t.Platform == platform && t.Id != _currentEditingTemplate.Id))
                {
                    other.IsDefault = false;
                }
            }

            SaveTemplatesToRepository();
            RefreshTemplateBindings();

            TemplateListBox.SelectedItem = _currentEditingTemplate;
            TemplateComboBox.SelectedItem = _currentEditingTemplate;

            StatusTextBlock.Text = $"Template \"{_currentEditingTemplate.Name}\" gespeichert.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler beim Speichern des Templates: {ex.Message}";
        }
    }

    private void LoadTemplateIntoEditor(Template? tmpl)
    {
        _currentEditingTemplate = tmpl;

        if (tmpl is null)
        {
            TemplateNameTextBox.Text = string.Empty;
            TemplatePlatformComboBox.SelectedItem = null;
            TemplateIsDefaultCheckBox.IsChecked = false;
            TemplateDescriptionTextBox.Text = string.Empty;
            TemplateBodyEditorTextBox.Text = string.Empty;
            return;
        }

        TemplateNameTextBox.Text = tmpl.Name;
        TemplatePlatformComboBox.SelectedItem = tmpl.Platform;
        TemplateIsDefaultCheckBox.IsChecked = tmpl.IsDefault;
        TemplateDescriptionTextBox.Text = tmpl.Description ?? string.Empty;
        TemplateBodyEditorTextBox.Text = tmpl.Body;
    }

    private void SaveTemplatesToRepository()
    {
        try
        {
            _templateRepository.Save(_loadedTemplates);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Templates konnten nicht gespeichert werden: {ex.Message}";
        }
    }

    private void RefreshTemplateBindings()
    {
        // Liste
        TemplateListBox.ItemsSource = null;
        TemplateListBox.ItemsSource = _loadedTemplates;

        // Auswahl im Upload-Tab
        TemplateComboBox.ItemsSource = null;
        TemplateComboBox.ItemsSource = _loadedTemplates;
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
            ScheduledTime = scheduledTime,
            ThumbnailPath = ThumbnailPathTextBox.Text
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
