using System;
using System.Linq;
using System.Windows.Controls;
using DCM.App.Models;
using DCM.YouTube;

namespace DCM.App;

public partial class MainWindow
{
    #region YouTube Konto & Playlists

    private async void YouTubeConnectButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            if (_youTubeClient.IsConnected)
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Disconnecting");
                await _youTubeClient.DisconnectAsync();
            }

            StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Connecting");
            await _youTubeClient.ConnectAsync(System.Threading.CancellationToken.None);
            UpdateYouTubeStatusText();
            await RefreshYouTubePlaylistsAsync();
            await RefreshYouTubeMetadataOptionsAsync();
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
            PresetsPageView?.SetPlaylistOptions(Array.Empty<YouTubePlaylistInfo>());
            await RefreshYouTubeMetadataOptionsAsync();
            StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Disconnected");
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.YouTube.DisconnectFailed", ex.Message);
        }
    }

    private async void YouTubeRefreshButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!_youTubeClient.IsConnected)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Disconnected");
            return;
        }

        StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Refreshing");

        try
        {
            await RefreshYouTubePlaylistsAsync();
            await RefreshYouTubeMetadataOptionsAsync();
            UpdateYouTubeStatsFromHistory();
            StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.RefreshComplete");
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.YouTube.RefreshFailed", ex.Message);
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
            AccountsPageView?.SetYouTubeConnectionState(true);
        }
        else
        {
            AccountsPageView?.SetYouTubeAccountStatus(LocalizationHelper.Get("Accounts.YouTube.Msg.NotConnected"));
            AccountsPageView?.SetYouTubeConnectionState(false);
        }
    }

    private async System.Threading.Tasks.Task RefreshYouTubePlaylistsAsync()
    {
        if (!_youTubeClient.IsConnected)
        {
            _youTubePlaylists.Clear();
            AccountsPageView?.ClearYouTubePlaylists();
            UploadView.PlaylistComboBox.ItemsSource = null;
            PresetsPageView?.SetPlaylistOptions(Array.Empty<YouTubePlaylistInfo>());
            return;
        }

        try
        {
            var playlists = await _youTubeClient.GetPlaylistsAsync(System.Threading.CancellationToken.None);

            _youTubePlaylists.Clear();
            _youTubePlaylists.AddRange(playlists);

            AccountsPageView?.SetYouTubePlaylists(_youTubePlaylists, _settings.DefaultPlaylistId);
            UploadView.PlaylistComboBox.ItemsSource = _youTubePlaylists;
            PresetsPageView?.SetPlaylistOptions(_youTubePlaylists);

            if (!string.IsNullOrWhiteSpace(_settings.DefaultPlaylistId))
            {
                var selected = _youTubePlaylists.FirstOrDefault(i => i.Id == _settings.DefaultPlaylistId);
                if (selected is not null)
                {
                    UploadView.PlaylistComboBox.SelectedItem = selected;
                }
            }

            _settings.YouTubeLastSyncUtc = DateTime.UtcNow;
            AccountsPageView?.SetYouTubeLastSync(_settings.YouTubeLastSyncUtc);
            ScheduleSettingsSave();
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.YouTube.LoadPlaylistsFailed", ex.Message);
        }
    }

    private async System.Threading.Tasks.Task RefreshYouTubeMetadataOptionsAsync()
    {
        var (localeKey, hl, region) = GetYouTubeLocaleInfo();

        _settings.YouTubeOptionsLocale = localeKey;
        _settings.YouTubeCategoryOptions.Clear();
        _settings.YouTubeLanguageOptions.Clear();
        _youTubeCategories.Clear();
        _youTubeLanguages.Clear();
        PresetsPageView?.SetCategoryOptions(_youTubeCategories);
        PresetsPageView?.SetLanguageOptions(_youTubeLanguages);
        ApplyUploadLanguageOptions(_youTubeLanguages);
        ScheduleSettingsSave();
        AccountsPageView?.SetYouTubeLocale(_settings.YouTubeOptionsLocale);

        if (!_youTubeClient.IsConnected)
        {
            return;
        }

        try
        {
            var categories = await _youTubeClient.GetVideoCategoriesAsync(region, hl, System.Threading.CancellationToken.None);
            var languages = await _youTubeClient.GetI18nLanguagesAsync(hl, System.Threading.CancellationToken.None);

            _settings.YouTubeCategoryOptions = categories.ToList();
            _settings.YouTubeLanguageOptions = languages.ToList();

            foreach (var option in _settings.YouTubeCategoryOptions)
            {
                if (!string.IsNullOrWhiteSpace(option.Code))
                {
                    _youTubeCategories.Add(new CategoryOption(option.Code, option.Name));
                }
            }

            foreach (var option in _settings.YouTubeLanguageOptions)
            {
                if (!string.IsNullOrWhiteSpace(option.Code))
                {
                    _youTubeLanguages.Add(new LanguageOption(option.Code, option.Name));
                }
            }

            PresetsPageView?.SetCategoryOptions(_youTubeCategories);
            PresetsPageView?.SetLanguageOptions(_youTubeLanguages);
            ApplyUploadLanguageOptions(_youTubeLanguages);
            ScheduleSettingsSave();
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.YouTube.LoadOptionsFailed", ex.Message);
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
            await RefreshYouTubeMetadataOptionsAsync();
            StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Restored");
        }
        catch
        {
            // Auto-Login ist nur Komfort
        }
    }

    #endregion
}
