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
    private readonly CancellationTokenSource _cts = new();

    public MainWindow()
    {
        InitializeComponent();

        _templateService = new TemplateService();
        _uploadService = new FakeYouTubeUploadService();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Videodatei auswählen",
            Filter = "Video Files|*.mp4;*.mkv;*.mov;*.avi|All Files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            VideoPathTextBox.Text = dialog.FileName;

            if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
            {
                var fileName = Path.GetFileNameWithoutExtension(dialog.FileName);
                TitleTextBox.Text = fileName;
            }
        }
    }

    private void GenerateTitleButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(VideoPathTextBox.Text))
        {
            MessageBox.Show("Bitte zuerst eine Videodatei auswählen.", "Hinweis",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var fileName = Path.GetFileNameWithoutExtension(VideoPathTextBox.Text);
        var dateString = DateTime.Now.ToString("yyyy-MM-dd");
        TitleTextBox.Text = $"{fileName} | Highlight | {dateString}";
    }

    private void ApplyTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildProjectFromUiOrShowError();
        if (project is null)
        {
            return;
        }

        // Einfaches Default-Template (später aus Settings)
        var template =
@"{{TITLE}}

Playlist: {{PLAYLIST}}
Plattform: {{PLATFORM}}
Sichtbarkeit: {{VISIBILITY}}

Tags: {{TAGS}}

Aufgenommen am: {{CREATED_AT}}
Geplanter Upload: {{DATE}}";

        var result = _templateService.ApplyTemplate(template, project);
        DescriptionTextBox.Text = result;
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        var project = BuildProjectFromUiOrShowError();
        if (project is null)
        {
            return;
        }

        SetUiBusy(true, "Upload wird vorbereitet...");

        try
        {
            var result = await _uploadService.UploadAsync(project, _cts.Token);

            if (result.Success)
            {
                StatusTextBlock.Text = $"Upload simuliert ✔ URL: {result.VideoUrl}";
                MessageBox.Show(
                    $"Upload erfolgreich (simuliert).\n\nVideo: {result.VideoUrl}",
                    "Upload",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                StatusTextBlock.Text = "Upload fehlgeschlagen.";
                MessageBox.Show(
                    result.ErrorMessage ?? "Unbekannter Fehler.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Fehler beim Upload.";
            MessageBox.Show(
                ex.Message,
                "Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    private UploadProject? BuildProjectFromUiOrShowError()
    {
        var videoPath = VideoPathTextBox.Text;
        var title = TitleTextBox.Text;
        var description = DescriptionTextBox.Text;

        if (string.IsNullOrWhiteSpace(videoPath))
        {
            MessageBox.Show("Bitte eine Videodatei angeben.", "Fehlende Daten",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBox.Show("Bitte einen Titel angeben.", "Fehlende Daten",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        var project = new UploadProject
        {
            Platform = PlatformType.YouTube,
            VideoFilePath = videoPath,
            Title = title,
            Description = description,
            Visibility = VideoVisibility.Unlisted,
            PlaylistId = null,
            ScheduledTime = null
        };

        // Später kannst du Tags aus einem UI-Feld einlesen
        project.SetTagsFromCsv("stream, highlight");

        try
        {
            project.Validate();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ungültige Projektdaten",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        return project;
    }

    private void SetUiBusy(bool isBusy, string? status = null)
    {
        BrowseButton.IsEnabled = !isBusy;
        GenerateTitleButton.IsEnabled = !isBusy;
        ApplyTemplateButton.IsEnabled = !isBusy;
        UploadButton.IsEnabled = !isBusy;

        StatusTextBlock.Text = status ?? string.Empty;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        _cts.Dispose();
        base.OnClosed(e);
    }
}
