using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
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
    private readonly IPlatformClient _youTubeClient;
    private AppSettings _settings = new();

    private Template[] _loadedTemplates = Array.Empty<Template>();

    public MainWindow()
    {
        InitializeComponent();

        // JSON-Konfigsystem initialisieren
        _settingsProvider = new JsonSettingsProvider();
        _templateRepository = new JsonTemplateRepository();
        _youTubeClient = new YouTubePlatformClient(new FakeYouTubeUploadService());

        LoadSettings();
        LoadTemplates();
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

    #region Events

    private void TemplateListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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

        // Tags können später über eigene UI kommen, Phase 1 reicht dieses Grundgerüst.
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
            // Silent fail – für Phase 1 nicht kritisch
        }
    }

    #endregion
}
