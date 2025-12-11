using System.Windows.Controls;
using DCM.YouTube;

namespace DCM.App;

public partial class MainWindow
{
    #region YouTube Konto & Playlists

    private async void YouTubeConnectButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Connecting");

        try
        {
            await _youTubeClient.ConnectAsync(System.Threading.CancellationToken.None);
            UpdateYouTubeStatusText();
            await RefreshYouTubePlaylistsAsync();
            StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Connected");
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.YouTube.ConnectFailed", ex.Message);
        }
    }

    private async void YouTubeDisconnectButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Disconnecting");

        try
        {
            await _youTubeClient.DisconnectAsync();
            UpdateYouTubeStatusText();
            _youTubePlaylists.Clear();
            AccountsPageView?.ClearYouTubePlaylists();
            UploadView.PlaylistComboBox.ItemsSource = null;
            StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Disconnected");
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.YouTube.DisconnectFailed", ex.Message);
        }
    }

    private void YouTubePlaylistsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = AccountsPageView?.SelectedYouTubePlaylist;
        if (item is YouTubePlaylistInfo playlist)
        {
            _settings.DefaultPlaylistId = playlist.Id;
            SaveSettings();
            StatusTextBlock.Text = LocalizationHelper.Format("Status.YouTube.DefaultPlaylistSet", playlist.Title);

            if (UploadView.PlaylistComboBox.ItemsSource is not null)
            {
                var selected = _youTubePlaylists.FirstOrDefault(p => p.Id == playlist.Id);
                if (selected is not null)
                {
                    UploadView.PlaylistComboBox.SelectedItem = selected;
                }
            }
        }
    }

    private void UpdateYouTubeStatusText()
    {
        if (_youTubeClient.IsConnected)
        {
            var status = string.IsNullOrWhiteSpace(_youTubeClient.ChannelTitle)
                ? LocalizationHelper.Get("Status.YouTube.Connected")
                : LocalizationHelper.Format("Status.YouTube.ConnectedAs", _youTubeClient.ChannelTitle);
            AccountsPageView?.SetYouTubeAccountStatus(status);
        }
        else
        {
            AccountsPageView?.SetYouTubeAccountStatus(LocalizationHelper.Get("Accounts.YouTube.Msg.NotConnected"));
        }
    }

    private async System.Threading.Tasks.Task RefreshYouTubePlaylistsAsync()
    {
        if (!_youTubeClient.IsConnected)
        {
            _youTubePlaylists.Clear();
            AccountsPageView?.ClearYouTubePlaylists();
            UploadView.PlaylistComboBox.ItemsSource = null;
            return;
        }

        try
        {
            var playlists = await _youTubeClient.GetPlaylistsAsync(System.Threading.CancellationToken.None);

            _youTubePlaylists.Clear();
            _youTubePlaylists.AddRange(playlists);

            AccountsPageView?.SetYouTubePlaylists(_youTubePlaylists, _settings.DefaultPlaylistId);
            UploadView.PlaylistComboBox.ItemsSource = _youTubePlaylists;

            if (!string.IsNullOrWhiteSpace(_settings.DefaultPlaylistId))
            {
                var selected = _youTubePlaylists.FirstOrDefault(i => i.Id == _settings.DefaultPlaylistId);
                if (selected is not null)
                {
                    UploadView.PlaylistComboBox.SelectedItem = selected;
                }
            }
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.YouTube.LoadPlaylistsFailed", ex.Message);
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
            StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Restored");
        }
        catch
        {
            // Auto-Login ist nur Komfort
        }
    }

    #endregion
}
