using System;
using System.Linq;
using System.Windows.Controls;
using DCM.App.Models;
using DCM.App.Infrastructure;
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
                _ui.Run(() =>
                {
                    StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Disconnecting");
                }, UiUpdatePolicy.StatusPriority);
                await _youTubeClient.DisconnectAsync().ConfigureAwait(false);
            }

            _ui.Run(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Connecting");
            }, UiUpdatePolicy.StatusPriority);
            await _youTubeClient.ConnectAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);
            await _ui.RunAsync(UpdateYouTubeStatusText, UiUpdatePolicy.StatusPriority);
            await RefreshYouTubePlaylistsAsync().ConfigureAwait(false);
            await RefreshYouTubeMetadataOptionsAsync().ConfigureAwait(false);
            _ui.Run(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Connected");
            }, UiUpdatePolicy.StatusPriority);
        }
        catch (System.Exception ex)
        {
            _ui.Run(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Format("Status.YouTube.ConnectFailed", ex.Message);
            }, UiUpdatePolicy.StatusPriority);
        }
    }

    private async void YouTubeDisconnectButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _ui.Run(() =>
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Disconnecting");
        }, UiUpdatePolicy.StatusPriority);

        try
        {
            await _youTubeClient.DisconnectAsync().ConfigureAwait(false);
            await _ui.RunAsync(() =>
            {
                UpdateYouTubeStatusText();
                _youTubePlaylists.Clear();
                AccountsPageView?.ClearYouTubePlaylists();
                UploadView.PlaylistComboBox.ItemsSource = null;
                PresetsPageView?.SetPlaylistOptions(Array.Empty<YouTubePlaylistInfo>());
            }, UiUpdatePolicy.StatusPriority);
            await RefreshYouTubeMetadataOptionsAsync().ConfigureAwait(false);
            _ui.Run(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Disconnected");
            }, UiUpdatePolicy.StatusPriority);
        }
        catch (System.Exception ex)
        {
            _ui.Run(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Format("Status.YouTube.DisconnectFailed", ex.Message);
            }, UiUpdatePolicy.StatusPriority);
        }
    }

    private async void YouTubeRefreshButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!_youTubeClient.IsConnected)
        {
            _ui.Run(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Disconnected");
            }, UiUpdatePolicy.StatusPriority);
            return;
        }

        _ui.Run(() =>
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Refreshing");
        }, UiUpdatePolicy.StatusPriority);

        try
        {
            await RefreshYouTubePlaylistsAsync().ConfigureAwait(false);
            await RefreshYouTubeMetadataOptionsAsync().ConfigureAwait(false);
            await _ui.RunAsync(() =>
            {
                UpdateYouTubeStatsFromHistory();
                StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.RefreshComplete");
            }, UiUpdatePolicy.StatusPriority);
        }
        catch (System.Exception ex)
        {
            _ui.Run(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Format("Status.YouTube.RefreshFailed", ex.Message);
            }, UiUpdatePolicy.StatusPriority);
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
            await _ui.RunAsync(() =>
            {
                _youTubePlaylists.Clear();
                AccountsPageView?.ClearYouTubePlaylists();
                UploadView.PlaylistComboBox.ItemsSource = null;
                PresetsPageView?.SetPlaylistOptions(Array.Empty<YouTubePlaylistInfo>());
            }, UiUpdatePolicy.StatusPriority);
            return;
        }

        try
        {
            var playlists = await _youTubeClient.GetPlaylistsAsync(System.Threading.CancellationToken.None)
                .ConfigureAwait(false);

            await _ui.RunAsync(() =>
            {
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
            }, UiUpdatePolicy.StatusPriority);
        }
        catch (System.Exception ex)
        {
            _ui.Run(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Format("Status.YouTube.LoadPlaylistsFailed", ex.Message);
            }, UiUpdatePolicy.StatusPriority);
        }
    }

    private async System.Threading.Tasks.Task RefreshYouTubeMetadataOptionsAsync()
    {
        var (localeKey, hl, region) = GetYouTubeLocaleInfo();

        await _ui.RunAsync(() =>
        {
            _settings.YouTubeOptionsLocale = localeKey;
            _settings.YouTubeCategoryOptions.Clear();
            _settings.YouTubeLanguageOptions.Clear();
            _youTubeCategories.Clear();
            _youTubeLanguages.Clear();
            PresetsPageView?.SetCategoryOptions(_youTubeCategories);
            PresetsPageView?.SetLanguageOptions(_youTubeLanguages);
            _categoryManager?.UpdateOptions(_youTubeCategories, _activeDraft?.CategoryId);
            _languageManager?.UpdateOptions(_youTubeLanguages, _activeDraft?.Language);
            ScheduleSettingsSave();
            AccountsPageView?.SetYouTubeLocale(_settings.YouTubeOptionsLocale);
        }, UiUpdatePolicy.StatusPriority);

        if (!_youTubeClient.IsConnected)
        {
            return;
        }

        try
        {
            var categories = await _youTubeClient.GetVideoCategoriesAsync(region, hl, System.Threading.CancellationToken.None)
                .ConfigureAwait(false);
            var languages = await _youTubeClient.GetI18nLanguagesAsync(hl, System.Threading.CancellationToken.None)
                .ConfigureAwait(false);


            await _ui.RunAsync(() =>
            {
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
                
                _categoryManager?.UpdateOptions(_youTubeCategories, _activeDraft?.CategoryId);
                _languageManager?.UpdateOptions(_youTubeLanguages, _activeDraft?.Language);
                
                ScheduleSettingsSave();
            }, UiUpdatePolicy.StatusPriority);
        }
        catch (System.Exception ex)
        {
            _ui.Run(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Format("Status.YouTube.LoadOptionsFailed", ex.Message);
            }, UiUpdatePolicy.StatusPriority);
        }
    }

    private async System.Threading.Tasks.Task TryAutoConnectYouTubeAsync()
    {
        try
        {
            var connected = await _youTubeClient.TryConnectSilentAsync(System.Threading.CancellationToken.None)
                .ConfigureAwait(false);
            if (!connected)
            {
                return;
            }

            await _ui.RunAsync(UpdateYouTubeStatusText, UiUpdatePolicy.StatusPriority);
            await RefreshYouTubePlaylistsAsync().ConfigureAwait(false);
            await RefreshYouTubeMetadataOptionsAsync().ConfigureAwait(false);
            _ui.Run(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.YouTube.Restored");
            }, UiUpdatePolicy.StatusPriority);
        }
        catch
        {
            // Auto-Login ist nur Komfort
        }
    }

    #endregion
}
