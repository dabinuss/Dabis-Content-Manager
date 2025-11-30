using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using DCM.Core.Models;
using DCM.Core.Services;
using DCM.YouTube;

namespace DCM.App;

public partial class MainWindow : Window
{
    private readonly TemplateService _templateService;
    private readonly IYouTubeUploadService _uploadService;
    private readonly UploadProject _project;
    private CancellationTokenSource? _uploadCts;

    private const string DefaultYouTubeDescriptionTemplate = """
{{TITLE}}

---

Hochgeladen mit Dabis Content Manager.

Sichtbarkeit: {{VISIBILITY}}
Playlist: {{PLAYLIST}}
Tags: {{TAGS}}
Geplant: {{DATE}}
Plattform: {{PLATFORM}}
Erstellt am: {{CREATED_AT}}
""";

    public MainWindow()
    {
        InitializeComponent();

        _templateService = new TemplateService();
        _uploadService = new FakeYouTubeUploadService();
        _project = new UploadProject
        {
            Platform = PlatformType.YouTube,
            Visibility = VideoVisibility.Unlisted
        };
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Videodatei auswählen",
            Filter =
                "Video-Dateien (*.mp4;*.mov;*.mkv;*.avi;*.webm)|*.mp4;*.mov;*.mkv;*.avi;*.webm|Alle Dateien (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            VideoPathTextBox.Text = dialog.FileName;
            _project.VideoFilePath = dialog.FileName;

            if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
            {
                TitleTextBox.Text = CreateTitleSuggestionFromFileName(dialog.FileName);
            }

            StatusTextBlock.Text = string.Empty;
        }
    }

    private void GenerateTitleButton_Click(object sender, RoutedEventArgs e)
    {
        string source = !string.IsNullOrWhiteSpace(VideoPathTextBox.Text)
            ? VideoPathTextBox.Text
            : TitleTextBox.Text;

        if (string.IsNullOrWhiteSpace(source))
        {
            StatusTextBlock.Text = "Bitte zuerst eine Videodatei wählen oder einen Titel eingeben.";
            return;
        }

        var suggestion = CreateTitleSuggestionFromFileName(source);
        TitleTextBox.Text = suggestion;
        StatusTextBlock.Text = "Titelvorschlag übernommen.";
    }

    private void ApplyTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SyncUiToProject();

            var descriptionFromTemplate = _templateService.ApplyTemplate(
                DefaultYouTubeDescriptionTemplate,
                _project);

            if (!string.IsNullOrWhiteSpace(DescriptionTextBox.Text))
            {
                DescriptionTextBox.Text += Environment.NewLine + Environment.NewLine + descriptionFromTemplate;
            }
            else
            {
                DescriptionTextBox.Text = descriptionFromTemplate;
            }

            _project.Description = DescriptionTextBox.Text;
            StatusTextBlock.Text = "Template angewendet.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler beim Anwenden des Templates: {ex.Message}";
        }
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        DisableUiForUpload();

        _uploadCts?.Cancel();
        _uploadCts = new CancellationTokenSource();

        try
        {
            SyncUiToProject();

            StatusTextBlock.Text = "Upload läuft...";

            var result = await _uploadService.UploadAsync(_project, _uploadCts.Token);

            if (result.Success)
            {
                StatusTextBlock.Text = $"Upload abgeschlossen: {result.VideoUrl}";
            }
            else
            {
                StatusTextBlock.Text = $"Upload fehlgeschlagen: {result.ErrorMessage}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Upload abgebrochen.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Fehler: {ex.Message}";
        }
        finally
        {
            EnableUiAfterUpload();
        }
    }

    private void SyncUiToProject()
    {
        _project.Title = TitleTextBox.Text.Trim();
        _project.Description = DescriptionTextBox.Text;

        // Phase 2: Wir halten es simpel.
        _project.Platform = PlatformType.YouTube;
        _project.Visibility = VideoVisibility.Unlisted;

        // Tags / Playlist / Scheduling kommen später mit eigener UI.
        // ScheduledTime, PlaylistId und Tags bleiben in der Standardeinstellung.
    }

    private static string CreateTitleSuggestionFromFileName(string pathOrTitle)
    {
        if (string.IsNullOrWhiteSpace(pathOrTitle))
        {
            return "Neues Video";
        }

        var fileName = Path.GetFileNameWithoutExtension(pathOrTitle);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = pathOrTitle;
        }

        var title = fileName
            .Replace('_', ' ')
            .Replace('-', ' ');

        while (title.Contains("  ", StringComparison.Ordinal))
        {
            title = title.Replace("  ", " ");
        }

        return title.Trim();
    }

    private void DisableUiForUpload()
    {
        UploadButton.IsEnabled = false;
        ApplyTemplateButton.IsEnabled = false;
        GenerateTitleButton.IsEnabled = false;
        BrowseButton.IsEnabled = false;
    }

    private void EnableUiAfterUpload()
    {
        UploadButton.IsEnabled = true;
        ApplyTemplateButton.IsEnabled = true;
        GenerateTitleButton.IsEnabled = true;
        BrowseButton.IsEnabled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _uploadCts?.Cancel();
        _uploadCts?.Dispose();
        base.OnClosed(e);
    }
}
