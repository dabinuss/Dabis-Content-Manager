using System;
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
    private AppSettings _settings = new();

    private Template[] _loadedTemplates = Array.Empty<Template>();

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

        LoadSettings();
        LoadTemplates();
        UpdateYouTubeStatusText(); // Anfangsstatus
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

    private void LoadTemplates()
    {
        try
        {
            _loadedTemplates = _templateRepository.Load().ToArray();

            TemplateListBox.ItemsSource = _loadedTemplates;
            TemplateListBox.SelectionChanged += TemplateListBox_SelectionChanged;

            var defaultTemplate = _loadedTemplates.FirstOrDefault(t => t.IsDefault && t.Platform == PlatformType.YouTube)
                                  ?? _loadedTemplates.FirstOrDefault(t => t.Platform == PlatformType.YouTube);

            if (defaultTemplate is not null)
            {
                TemplateListBox.SelectedItem = defaultTemplate;
                TemplateBodyTextBox.Text = defaultTemplate.Body;
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
        if (TemplateListBox.SelectedItem is not Template tmpl)
        {
            StatusTextBlock.Text = "Kein Template ausgewählt.";
            return;
        }

        var project = BuildUploadProjectFromUi();
        var result = _templateService.ApplyTemplate(tmpl.Body, project);

        DescriptionTextBox.Text = result;
        StatusTextBlock.Text = $"Template \"{tmpl.Name}\" angewendet.";
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildUploadProjectFromUi();

        try
        {
            project.Validate();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehlerhafte Eingaben: {ex.Message}";
            return;
        }

        if (!_youTubeClient.IsConnected)
        {
            StatusTextBlock.Text = "Bitte zuerst im Tab \"Konten\" mit YouTube verbinden.";
            return;
        }

        StatusTextBlock.Text = "Upload wird vorbereitet...";

        try
        {
            var result = await _youTubeClient.UploadAsync(project, CancellationToken.None);

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
            YouTubePlaylistsListBox.ItemsSource = null;
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
        }
    }

    #endregion

    #region Helpers

    private UploadProject BuildUploadProjectFromUi()
    {
        var project = new UploadProject
        {
            VideoFilePath = VideoPathTextBox.Text ?? string.Empty,
            Title = TitleTextBox.Text ?? string.Empty,
            Description = DescriptionTextBox.Text ?? string.Empty,
            Platform = PlatformType.YouTube,
            Visibility = VideoVisibility.Unlisted,
            PlaylistId = _settings.DefaultPlaylistId,
            ScheduledTime = null // später über UI steuerbar
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
            YouTubePlaylistsListBox.ItemsSource = null;
            return;
        }

        try
        {
            var playlists = await _youTubeClient.GetPlaylistsAsync(CancellationToken.None);

            var items = playlists
                .Select(p => new YouTubePlaylistListItem(p.Id, p.Title))
                .ToList();

            YouTubePlaylistsListBox.ItemsSource = items;

            if (!string.IsNullOrWhiteSpace(_settings.DefaultPlaylistId))
            {
                var selected = items.FirstOrDefault(i => i.Id == _settings.DefaultPlaylistId);
                if (selected is not null)
                {
                    YouTubePlaylistsListBox.SelectedItem = selected;
                }
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler beim Laden der Playlists: {ex.Message}";
        }
    }

    #endregion
}
