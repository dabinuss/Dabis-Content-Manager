using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.Win32;
using DCM.Core.Configuration;
using DCM.Core.Models;
using DCM.Core.Services;
using DCM.YouTube;
using WinForms = System.Windows.Forms;

namespace DCM.App;

public partial class MainWindow : Window
{
    private const long MaxThumbnailSizeBytes = 1 * 1024 * 1024; // 1 MB

    private readonly TemplateService _templateService = new();
    private readonly ITemplateRepository _templateRepository;
    private readonly ISettingsProvider _settingsProvider;
    private readonly YouTubePlatformClient _youTubeClient;
    private readonly IContentSuggestionService _contentSuggestionService;
    private readonly UploadHistoryService _uploadHistoryService;
    private readonly ILlmService _llmService;
    private UploadService _uploadService;
    private AppSettings _settings = new();

    private readonly List<Template> _loadedTemplates = new();
    private Template? _currentEditingTemplate;

    private readonly List<YouTubePlaylistListItem> _youTubePlaylists = new();

    private List<UploadHistoryEntry> _allHistoryEntries = new();

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
        _uploadHistoryService = new UploadHistoryService();

        LoadSettings();

        // LLM-Service initialisieren
        _llmService = new DummyLlmService(_settings.Llm);
        _contentSuggestionService = new SimpleContentSuggestionService(_llmService);

        _uploadService = new UploadService(new IPlatformClient[] { _youTubeClient }, _templateService);

        InitializePlatformComboBox();
        InitializeLanguageComboBox();
        InitializeSchedulingDefaults();
        LoadTemplates();
        UpdateYouTubeStatusText();
        LoadUploadHistory();

        // Auto-Reconnect nach dem Laden des Fensters
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_settings.AutoConnectYouTube)
        {
            await TryAutoConnectYouTubeAsync();
        }
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

        // Safety: Persona nie null lassen
        _settings.Persona ??= new ChannelPersona();
        _settings.Llm ??= new LlmSettings();

        ApplySettingsToUi();
    }

    private void ApplySettingsToUi()
    {
        // App-Einstellungen-Tab
        if (DefaultVideoFolderTextBox is not null)
        {
            DefaultVideoFolderTextBox.Text = _settings.DefaultVideoFolder ?? string.Empty;
        }

        if (DefaultThumbnailFolderTextBox is not null)
        {
            DefaultThumbnailFolderTextBox.Text = _settings.DefaultThumbnailFolder ?? string.Empty;
        }

        if (DefaultSchedulingTimeTextBox is not null)
        {
            DefaultSchedulingTimeTextBox.Text = string.IsNullOrWhiteSpace(_settings.DefaultSchedulingTime)
                ? "18:00"
                : _settings.DefaultSchedulingTime;
        }

        if (DefaultVisibilityComboBox is not null)
        {
            var items = DefaultVisibilityComboBox.Items.OfType<ComboBoxItem>().ToList();
            var selected = items.FirstOrDefault(i =>
                i.Tag is VideoVisibility vv && vv == _settings.DefaultVisibility);
            if (selected is not null)
            {
                DefaultVisibilityComboBox.SelectedItem = selected;
            }
        }

        if (ConfirmBeforeUploadCheckBox is not null)
        {
            ConfirmBeforeUploadCheckBox.IsChecked = _settings.ConfirmBeforeUpload;
        }

        if (AutoApplyDefaultTemplateCheckBox is not null)
        {
            AutoApplyDefaultTemplateCheckBox.IsChecked = _settings.AutoApplyDefaultTemplate;
        }

        if (OpenBrowserAfterUploadCheckBox is not null)
        {
            OpenBrowserAfterUploadCheckBox.IsChecked = _settings.OpenBrowserAfterUpload;
        }

        if (AutoConnectYouTubeCheckBox is not null)
        {
            AutoConnectYouTubeCheckBox.IsChecked = _settings.AutoConnectYouTube;
        }

        // Kanalprofil-Tab
        var persona = _settings.Persona ?? new ChannelPersona();
        _settings.Persona = persona;

        if (ChannelPersonaNameTextBox is not null)
        {
            ChannelPersonaNameTextBox.Text = persona.Name ?? string.Empty;
        }

        if (ChannelPersonaChannelNameTextBox is not null)
        {
            ChannelPersonaChannelNameTextBox.Text = persona.ChannelName ?? string.Empty;
        }

        if (ChannelPersonaLanguageTextBox is not null)
        {
            ChannelPersonaLanguageTextBox.Text = persona.Language ?? string.Empty;
        }

        if (ChannelPersonaToneOfVoiceTextBox is not null)
        {
            ChannelPersonaToneOfVoiceTextBox.Text = persona.ToneOfVoice ?? string.Empty;
        }

        if (ChannelPersonaContentTypeTextBox is not null)
        {
            ChannelPersonaContentTypeTextBox.Text = persona.ContentType ?? string.Empty;
        }

        if (ChannelPersonaTargetAudienceTextBox is not null)
        {
            ChannelPersonaTargetAudienceTextBox.Text = persona.TargetAudience ?? string.Empty;
        }

        if (ChannelPersonaAdditionalInstructionsTextBox is not null)
        {
            ChannelPersonaAdditionalInstructionsTextBox.Text = persona.AdditionalInstructions ?? string.Empty;
        }
    }

    private void InitializePlatformComboBox()
    {
        // Upload-Tab
        PlatformComboBox.ItemsSource = Enum.GetValues(typeof(PlatformType));
        PlatformComboBox.SelectedItem = _settings.DefaultPlatform;

        // Templates-Tab
        TemplatePlatformComboBox.ItemsSource = Enum.GetValues(typeof(PlatformType));

        // Sichtbarkeit-Combo im Upload-Tab nach Settings
        if (VisibilityComboBox is not null)
        {
            var items = VisibilityComboBox.Items.OfType<ComboBoxItem>().ToList();
            var selected = items.FirstOrDefault(i =>
                i.Tag is VideoVisibility vv && vv == _settings.DefaultVisibility);
            if (selected is not null)
            {
                VisibilityComboBox.SelectedItem = selected;
            }
        }
    }

    private void InitializeLanguageComboBox()
    {
        if (ChannelPersonaLanguageTextBox is null)
            return;

        // Nur neutrale Sprachen (keine Länder-Kombinationen wie "Deutsch (Belgien)")
        var cultures = CultureInfo
            .GetCultures(CultureTypes.NeutralCultures)
            .Where(c => !string.IsNullOrWhiteSpace(c.Name)) // Invariant ausschließen
            .OrderBy(c => c.DisplayName)
            .Select(c => $"{c.DisplayName} ({c.Name})")
            .ToList();

        ChannelPersonaLanguageTextBox.ItemsSource = cultures;
        // Text bleibt das, was wir in ApplySettingsToUi gesetzt haben (ComboBox ist editierbar)
    }

    private void InitializeSchedulingDefaults()
    {
        // Default: nächster Tag, Uhrzeit aus Settings oder 18:00 Uhr
        TimeSpan defaultTime;

        if (!string.IsNullOrWhiteSpace(_settings.DefaultSchedulingTime) &&
            TimeSpan.TryParse(_settings.DefaultSchedulingTime, CultureInfo.CurrentCulture, out var parsed))
        {
            defaultTime = parsed;
        }
        else
        {
            defaultTime = new TimeSpan(18, 0, 0);
        }

        ScheduleDatePicker.SelectedDate = DateTime.Today.AddDays(1);
        ScheduleTimeTextBox.Text = defaultTime.ToString(@"hh\:mm");
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

    private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settings.AutoApplyDefaultTemplate)
            return;

        if (TemplateComboBox.SelectedItem is not Template tmpl)
            return;

        if (!string.IsNullOrWhiteSpace(DescriptionTextBox.Text))
            return;

        var project = BuildUploadProjectFromUi(includeScheduling: false);
        var result = _templateService.ApplyTemplate(tmpl.Body, project);
        DescriptionTextBox.Text = result;
        StatusTextBlock.Text = $"Template \"{tmpl.Name}\" automatisch angewendet.";
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        // InitialDirectory-Priorität:
        // 1) DefaultVideoFolder (falls gesetzt)
        // 2) LastVideoFolder
        // 3) Eigene Videos
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
            // DefaultThumbnailFolder bleibt über den App-Settings-Tab konfigurierbar.
        }
    }

    private void ThumbnailClearButton_Click(object sender, RoutedEventArgs e)
    {
        ThumbnailPathTextBox.Text = string.Empty;
    }

    private async void GenerateTitleButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);

        _settings.Persona ??= new ChannelPersona();

        StatusTextBlock.Text = "Generiere Titelvorschläge...";

        try
        {
            var titles = await _contentSuggestionService.SuggestTitlesAsync(project, _settings.Persona);

            if (titles is null || titles.Count == 0)
            {
                TitleTextBox.Text = "[Keine Vorschläge]";
                StatusTextBlock.Text = "Keine Titelvorschläge erhalten.";
                return;
            }

            TitleTextBox.Text = titles[0];
            StatusTextBlock.Text = $"Titelvorschlag eingefügt. ({titles.Count} Vorschläge)";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler bei Titelgenerierung: {ex.Message}";
        }
    }

    private async void GenerateDescriptionButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);

        _settings.Persona ??= new ChannelPersona();

        StatusTextBlock.Text = "Generiere Beschreibungsvorschlag...";

        try
        {
            var description = await _contentSuggestionService.SuggestDescriptionAsync(project, _settings.Persona);

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
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler bei Beschreibungsgenerierung: {ex.Message}";
        }
    }

    private async void GenerateTagsButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildUploadProjectFromUi(includeScheduling: false);

        _settings.Persona ??= new ChannelPersona();

        StatusTextBlock.Text = "Generiere Tag-Vorschläge...";

        try
        {
            var tags = await _contentSuggestionService.SuggestTagsAsync(project, _settings.Persona);

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
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler bei Tag-Generierung: {ex.Message}";
        }
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
        // Optionale Bestätigung vor dem Upload
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

            // Upload-Historie speichern
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

    #region Events – App-Einstellungen & Kanalprofil

    private void DefaultVideoFolderBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        string initialPath;

        if (!string.IsNullOrWhiteSpace(_settings.DefaultVideoFolder) &&
            Directory.Exists(_settings.DefaultVideoFolder))
        {
            initialPath = _settings.DefaultVideoFolder;
        }
        else if (!string.IsNullOrWhiteSpace(_settings.LastVideoFolder) &&
                 Directory.Exists(_settings.LastVideoFolder))
        {
            initialPath = _settings.LastVideoFolder;
        }
        else
        {
            initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        }

        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Standard-Videoordner auswählen",
            SelectedPath = initialPath,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            DefaultVideoFolderTextBox.Text = dialog.SelectedPath;
            _settings.DefaultVideoFolder = dialog.SelectedPath;
            SaveSettings();
            StatusTextBlock.Text = "Standard-Videoordner aktualisiert.";
        }
    }

    private void DefaultThumbnailFolderBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        string initialPath;

        if (!string.IsNullOrWhiteSpace(_settings.DefaultThumbnailFolder) &&
            Directory.Exists(_settings.DefaultThumbnailFolder))
        {
            initialPath = _settings.DefaultThumbnailFolder;
        }
        else if (!string.IsNullOrWhiteSpace(_settings.LastVideoFolder) &&
                 Directory.Exists(_settings.LastVideoFolder))
        {
            initialPath = _settings.LastVideoFolder;
        }
        else
        {
            initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        }

        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Standard-Thumbnailordner auswählen",
            SelectedPath = initialPath,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            DefaultThumbnailFolderTextBox.Text = dialog.SelectedPath;
            _settings.DefaultThumbnailFolder = dialog.SelectedPath;
            SaveSettings();
            StatusTextBlock.Text = "Standard-Thumbnailordner aktualisiert.";
        }
    }

    private void AppSettingsSaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.DefaultVideoFolder = string.IsNullOrWhiteSpace(DefaultVideoFolderTextBox.Text)
            ? null
            : DefaultVideoFolderTextBox.Text.Trim();

        _settings.DefaultThumbnailFolder = string.IsNullOrWhiteSpace(DefaultThumbnailFolderTextBox.Text)
            ? null
            : DefaultThumbnailFolderTextBox.Text.Trim();

        _settings.DefaultSchedulingTime = string.IsNullOrWhiteSpace(DefaultSchedulingTimeTextBox.Text)
            ? null
            : DefaultSchedulingTimeTextBox.Text.Trim();

        if (DefaultVisibilityComboBox.SelectedItem is ComboBoxItem visItem &&
            visItem.Tag is VideoVisibility visibility)
        {
            _settings.DefaultVisibility = visibility;
        }
        else
        {
            _settings.DefaultVisibility = VideoVisibility.Unlisted;
        }

        _settings.ConfirmBeforeUpload = ConfirmBeforeUploadCheckBox.IsChecked == true;
        _settings.AutoApplyDefaultTemplate = AutoApplyDefaultTemplateCheckBox.IsChecked == true;
        _settings.OpenBrowserAfterUpload = OpenBrowserAfterUploadCheckBox.IsChecked == true;
        _settings.AutoConnectYouTube = AutoConnectYouTubeCheckBox.IsChecked == true;

        SaveSettings();
        StatusTextBlock.Text = "App-Einstellungen gespeichert.";
    }

    private void ChannelProfileSaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Persona ??= new ChannelPersona();
        var persona = _settings.Persona;

        persona.Name = string.IsNullOrWhiteSpace(ChannelPersonaNameTextBox.Text)
            ? null
            : ChannelPersonaNameTextBox.Text.Trim();

        persona.ChannelName = string.IsNullOrWhiteSpace(ChannelPersonaChannelNameTextBox.Text)
            ? null
            : ChannelPersonaChannelNameTextBox.Text.Trim();

        persona.Language = string.IsNullOrWhiteSpace(ChannelPersonaLanguageTextBox.Text)
            ? null
            : ChannelPersonaLanguageTextBox.Text.Trim();

        persona.ToneOfVoice = string.IsNullOrWhiteSpace(ChannelPersonaToneOfVoiceTextBox.Text)
            ? null
            : ChannelPersonaToneOfVoiceTextBox.Text.Trim();

        persona.ContentType = string.IsNullOrWhiteSpace(ChannelPersonaContentTypeTextBox.Text)
            ? null
            : ChannelPersonaContentTypeTextBox.Text.Trim();

        persona.TargetAudience = string.IsNullOrWhiteSpace(ChannelPersonaTargetAudienceTextBox.Text)
            ? null
            : ChannelPersonaTargetAudienceTextBox.Text.Trim();

        persona.AdditionalInstructions = string.IsNullOrWhiteSpace(ChannelPersonaAdditionalInstructionsTextBox.Text)
            ? null
            : ChannelPersonaAdditionalInstructionsTextBox.Text.Trim();

        SaveSettings();
        StatusTextBlock.Text = "Kanalprofil gespeichert.";
    }

    #endregion

    #region Upload Progress UI

    private void ShowUploadProgress(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowUploadProgress(message));
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
            Dispatcher.Invoke(() => ReportUploadProgress(info));
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
            Dispatcher.Invoke(HideUploadProgress);
            return;
        }

        UploadProgressBar.Visibility = Visibility.Collapsed;
        UploadProgressLabel.Visibility = Visibility.Collapsed;
        UploadProgressBar.IsIndeterminate = false;
        UploadProgressBar.Value = 0;
        UploadProgressLabel.Text = string.Empty;
    }

    #endregion

    #region Helpers & History

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
        var visibility = _settings.DefaultVisibility;
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
            ThumbnailPath = ThumbnailPathTextBox.Text,
            TranscriptText = TranscriptTextBox.Text
        };

        // Tags aus UI übernehmen
        if (TagsTextBox is not null)
        {
            project.SetTagsFromCsv(TagsTextBox.Text ?? string.Empty);
        }

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
            return;

        IEnumerable<UploadHistoryEntry> filtered = _allHistoryEntries;

        // Plattform-Filter
        if (HistoryPlatformFilterComboBox?.SelectedItem is ComboBoxItem pItem &&
            pItem.Tag is PlatformType platformFilter)
        {
            filtered = filtered.Where(e => e.Platform == platformFilter);
        }

        // Status-Filter
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
                // Fehler beim Öffnen des Browsers ignorieren.
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
            // Fehler beim Öffnen des Browsers ignorieren.
        }

        e.Handled = true;
    }

    #endregion
}