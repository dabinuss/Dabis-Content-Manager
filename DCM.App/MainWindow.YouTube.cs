using System.Windows.Controls;
using DCM.YouTube;

namespace DCM.App;

public partial class MainWindow
{
    #region YouTube Konto & Playlists

    private async void YouTubeConnectButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        StatusTextBlock.Text = "Verbinde mit YouTube...";

        try
        {
            await _youTubeClient.ConnectAsync(System.Threading.CancellationToken.None);
            UpdateYouTubeStatusText();
            await RefreshYouTubePlaylistsAsync();
            StatusTextBlock.Text = "Mit YouTube verbunden.";
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = $"YouTube-Verbindung fehlgeschlagen: {ex.Message}";
        }
    }

    private async void YouTubeDisconnectButton_Click(object sender, System.Windows.RoutedEventArgs e)
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
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = $"Trennen fehlgeschlagen: {ex.Message}";
        }
    }

    private void YouTubePlaylistsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (YouTubePlaylistsListBox.SelectedItem is YouTubePlaylistInfo item)
        {
            _settings.DefaultPlaylistId = item.Id;
            SaveSettings();
            StatusTextBlock.Text = $"Standard-Playlist gesetzt: {item.Title}";

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

    private void UpdateYouTubeStatusText()
    {
        if (_youTubeClient.IsConnected)
        {
            YouTubeAccountStatusTextBlock.Text = string.IsNullOrWhiteSpace(_youTubeClient.ChannelTitle)
                ? "Mit YouTube verbunden."
                : $"Verbunden als: {_youTubeClient.ChannelTitle}";

            // Sidebar Service Icon aktualisieren
            if (YouTubeConnectionIndicator is not null)
            {
                YouTubeConnectionIndicator.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x2B, 0xA6, 0x40)); // SuccessBrush grün
            }
            if (YouTubeServiceIcon is not null)
            {
                YouTubeServiceIcon.ToolTip = string.IsNullOrWhiteSpace(_youTubeClient.ChannelTitle)
                    ? "YouTube: Verbunden"
                    : $"YouTube: {_youTubeClient.ChannelTitle}";
                YouTubeServiceIcon.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x3F, 0x3F, 0x3F));
            }
        }
        else
        {
            YouTubeAccountStatusTextBlock.Text = "Nicht mit YouTube verbunden.";

            // Sidebar Service Icon aktualisieren
            if (YouTubeConnectionIndicator is not null)
            {
                YouTubeConnectionIndicator.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x71, 0x71, 0x71)); // TextMutedBrush grau
            }
            if (YouTubeServiceIcon is not null)
            {
                YouTubeServiceIcon.ToolTip = "YouTube: Nicht verbunden";
                YouTubeServiceIcon.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x3F, 0x3F, 0x3F));
            }
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
            var playlists = await _youTubeClient.GetPlaylistsAsync(System.Threading.CancellationToken.None);

            _youTubePlaylists.Clear();
            _youTubePlaylists.AddRange(playlists);

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
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = $"Fehler beim Laden der Playlists: {ex.Message}";
        }
    }

    private async System.Threading.Tasks.Task TryAutoConnectYouTubeAsync()
    {
        try
        {
            var connected = await _youTubeClient.TryConnectSilentAsync(System.Threading.CancellationToken.None);
            if (!connected)
            {
                return;
            }

            UpdateYouTubeStatusText();
            await RefreshYouTubePlaylistsAsync();
            StatusTextBlock.Text = "YouTube-Verbindung wiederhergestellt.";
        }
        catch
        {
            // Auto-Login ist nur Komfort
        }
    }

    #endregion
}