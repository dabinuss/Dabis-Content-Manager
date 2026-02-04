using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Core.Logging;
using DCM.Llm;
using DCM.Transcription;

namespace DCM.App;

public partial class SetupDialog : Window
{
    private readonly IAppLogger _logger;
    private readonly AppSettings _settings;
    private readonly ITranscriptionService? _transcriptionService;
    private readonly HttpClient _httpClient;

    private LlmModelManager? _llmModelManager;
    private CancellationTokenSource? _transcriptionDownloadCts;
    private CancellationTokenSource? _llmDownloadCts;

    private bool _isTranscriptionDownloading;
    private bool _isLlmDownloading;
    private bool _transcriptionInstalled;
    private bool _llmInstalled;

    public bool DontShowAgain => DontShowAgainCheckBox.IsChecked == true;

    public SetupDialog(
        IAppLogger logger,
        AppSettings settings,
        ITranscriptionService? transcriptionService)
    {
        InitializeComponent();

        _logger = logger;
        _settings = settings;
        _transcriptionService = transcriptionService;
        _httpClient = new HttpClient();
        _llmModelManager = new LlmModelManager(_httpClient, _logger);

        Loaded += SetupDialog_Loaded;
    }

    private void SetupDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // Clipping für abgerundete Ecken setzen
        UpdateClip();
        RootBorder.SizeChanged += (s, args) => UpdateClip();

        // Checkbox-Status aus Einstellungen laden
        DontShowAgainCheckBox.IsChecked = _settings.SkipSetupDialog;

        CheckInstallationStatus();
    }

    private void UpdateClip()
    {
        RootBorder.Clip = new RectangleGeometry
        {
            RadiusX = 10,
            RadiusY = 10,
            Rect = new Rect(0, 0, RootBorder.ActualWidth, RootBorder.ActualHeight)
        };
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CheckInstallationStatus()
    {
        // Transkription Status prüfen
        var whisperStatus = _transcriptionService?.GetDependencyStatus();
        _transcriptionInstalled = whisperStatus?.AllAvailable ?? false;
        UpdateTranscriptionStatus();

        // LLM Status prüfen - gleiche Logik wie IsLlmReady() im Header
        var llmSettings = _settings.Llm ?? new LlmSettings();
        var effectivePath = llmSettings.GetEffectiveModelPath();
        _llmInstalled = !string.IsNullOrWhiteSpace(effectivePath) && File.Exists(effectivePath);
        UpdateLlmStatus();
    }

    private void UpdateTranscriptionStatus()
    {
        if (_transcriptionInstalled)
        {
            TranscriptionStatusIndicator.Fill = (Brush)FindResource("SuccessBrush");
            TranscriptionStatusText.Text = LocalizationHelper.Get("Setup.Status.Installed");
            TranscriptionInstallButton.Content = LocalizationHelper.Get("Setup.Button.Installed");
            TranscriptionInstallButton.IsEnabled = false;
        }
        else if (_isTranscriptionDownloading)
        {
            TranscriptionStatusIndicator.Fill = (Brush)FindResource("PrimaryBrush");
            TranscriptionStatusText.Text = LocalizationHelper.Get("Setup.Status.Downloading");
            TranscriptionInstallButton.Content = LocalizationHelper.Get("Setup.Button.Cancel");
            TranscriptionInstallButton.IsEnabled = true;
        }
        else
        {
            TranscriptionStatusIndicator.Fill = (Brush)FindResource("TextMutedBrush");
            TranscriptionStatusText.Text = LocalizationHelper.Get("Setup.Status.NotInstalled");
            TranscriptionInstallButton.Content = LocalizationHelper.Get("Setup.Button.Install");
            TranscriptionInstallButton.IsEnabled = true;
        }
    }

    private void UpdateLlmStatus()
    {
        if (_llmInstalled)
        {
            LlmStatusIndicator.Fill = (Brush)FindResource("SuccessBrush");
            LlmStatusText.Text = LocalizationHelper.Get("Setup.Status.Installed");
            LlmInstallButton.Content = LocalizationHelper.Get("Setup.Button.Installed");
            LlmInstallButton.IsEnabled = false;
        }
        else if (_isLlmDownloading)
        {
            LlmStatusIndicator.Fill = (Brush)FindResource("PrimaryBrush");
            LlmStatusText.Text = LocalizationHelper.Get("Setup.Status.Downloading");
            LlmInstallButton.Content = LocalizationHelper.Get("Setup.Button.Cancel");
            LlmInstallButton.IsEnabled = true;
        }
        else
        {
            LlmStatusIndicator.Fill = (Brush)FindResource("TextMutedBrush");
            LlmStatusText.Text = LocalizationHelper.Get("Setup.Status.NotInstalled");
            LlmInstallButton.Content = LocalizationHelper.Get("Setup.Button.Install");
            LlmInstallButton.IsEnabled = true;
        }
    }

    private async void TranscriptionInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTranscriptionDownloading)
        {
            // Abbrechen
            _transcriptionDownloadCts?.Cancel();
            return;
        }

        if (_transcriptionInstalled || _transcriptionService is null)
        {
            return;
        }

        _isTranscriptionDownloading = true;
        UpdateTranscriptionStatus();

        TranscriptionProgressBar.Visibility = Visibility.Visible;
        TranscriptionProgressText.Visibility = Visibility.Visible;
        TranscriptionProgressBar.Value = 0;

        _transcriptionDownloadCts = new CancellationTokenSource();

        var progress = new Progress<DependencyDownloadProgress>(p =>
        {
            Dispatcher.Invoke(() =>
            {
                TranscriptionProgressBar.Value = p.Percent;

                var sizeInfo = p.TotalBytes > 0
                    ? $"{p.BytesDownloaded / 1024.0 / 1024.0:F1} / {p.TotalBytes / 1024.0 / 1024.0:F1} MB"
                    : $"{p.BytesDownloaded / 1024.0 / 1024.0:F1} MB";

                TranscriptionProgressText.Text = $"{p.Percent:F0}% - {sizeInfo}";
            });
        });

        try
        {
            var modelSize = _settings.Transcription?.ModelSize ?? WhisperModelSize.Small;
            var success = await _transcriptionService.EnsureDependenciesAsync(modelSize, progress, _transcriptionDownloadCts.Token);

            if (success)
            {
                _transcriptionInstalled = true;
                _logger.Info("Transkriptions-Modell erfolgreich installiert", "Setup");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Transkriptions-Download abgebrochen", "Setup");
        }
        catch (Exception ex)
        {
            _logger.Error($"Transkriptions-Download fehlgeschlagen: {ex.Message}", "Setup", ex);
        }
        finally
        {
            _isTranscriptionDownloading = false;
            TranscriptionProgressBar.Visibility = Visibility.Collapsed;
            TranscriptionProgressText.Visibility = Visibility.Collapsed;
            UpdateTranscriptionStatus();

            _transcriptionDownloadCts?.Dispose();
            _transcriptionDownloadCts = null;
        }
    }

    private async void LlmInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isLlmDownloading)
        {
            // Abbrechen
            _llmDownloadCts?.Cancel();
            return;
        }

        if (_llmInstalled)
        {
            return;
        }

        _isLlmDownloading = true;
        UpdateLlmStatus();

        LlmProgressBar.Visibility = Visibility.Visible;
        LlmProgressText.Visibility = Visibility.Visible;
        LlmProgressBar.Value = 0;

        _llmDownloadCts = new CancellationTokenSource();
        _llmModelManager ??= new LlmModelManager(_httpClient, _logger);

        var preset = _settings.Llm?.ModelPreset ?? LlmModelPreset.Phi3Mini4kQ4;

        var progress = new Progress<LlmModelDownloadProgress>(p =>
        {
            Dispatcher.Invoke(() =>
            {
                if (p.IsError)
                {
                    LlmProgressText.Text = p.Message ?? "Download fehlgeschlagen";
                }
                else
                {
                    LlmProgressBar.Value = p.Percent;

                    var sizeInfo = p.TotalBytes > 0
                        ? $"{p.BytesDownloaded / 1024.0 / 1024.0:F1} / {p.TotalBytes / 1024.0 / 1024.0:F1} MB"
                        : $"{p.BytesDownloaded / 1024.0 / 1024.0:F1} MB";

                    LlmProgressText.Text = $"{p.Percent:F0}% - {sizeInfo}";
                }
            });
        });

        try
        {
            var success = await _llmModelManager.DownloadAsync(preset, progress, _llmDownloadCts.Token);

            if (success)
            {
                _llmInstalled = true;

                // Settings aktualisieren
                _settings.Llm ??= new LlmSettings();
                _settings.Llm.ModelPreset = preset;
                _settings.Llm.Mode = LlmMode.Local;

                _logger.Info("LLM-Modell erfolgreich installiert", "Setup");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("LLM-Download abgebrochen", "Setup");
        }
        catch (Exception ex)
        {
            _logger.Error($"LLM-Download fehlgeschlagen: {ex.Message}", "Setup", ex);
        }
        finally
        {
            _isLlmDownloading = false;
            LlmProgressBar.Visibility = Visibility.Collapsed;
            LlmProgressText.Visibility = Visibility.Collapsed;
            UpdateLlmStatus();

            _llmDownloadCts?.Dispose();
            _llmDownloadCts = null;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _transcriptionDownloadCts?.Cancel();
        _transcriptionDownloadCts?.Dispose();

        _llmDownloadCts?.Cancel();
        _llmDownloadCts?.Dispose();

        _httpClient.Dispose();

        base.OnClosed(e);
    }
}
