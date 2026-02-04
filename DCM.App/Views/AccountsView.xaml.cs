using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DCM.Core.Models;
using DCM.YouTube;

namespace DCM.App.Views;

public partial class AccountsView : UserControl
{
    private bool _isYouTubeConnected;
    private bool _isAutoConnectBinding;
    private bool _isYouTubeSettingsBinding;

    public AccountsView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? AccountsServiceTabChecked;
    public event RoutedEventHandler? YouTubeConnectButtonClicked;
    public event RoutedEventHandler? YouTubeDisconnectButtonClicked;
    public event SelectionChangedEventHandler? YouTubePlaylistsSelectionChanged;
    public event SelectionChangedEventHandler? YouTubeDefaultVisibilitySelectionChanged;
    public event TextChangedEventHandler? YouTubeDefaultPublishTimeTextChanged;
    public event EventHandler<bool>? YouTubeAutoConnectToggled;
    public event RoutedEventHandler? YouTubeRefreshButtonClicked;
    public event RoutedEventHandler? YouTubeOpenLogsButtonClicked;
    public YouTubePlaylistInfo? SelectedYouTubePlaylist => YouTubePlaylistsListBox.SelectedItem as YouTubePlaylistInfo;

    public void SetServiceSelection(bool youTubeSelected, bool tikTokSelected, bool instagramSelected)
    {
        AccountsYouTubeTab.IsChecked = youTubeSelected;
        AccountsTikTokTab.IsChecked = tikTokSelected;
        AccountsInstagramTab.IsChecked = instagramSelected;

        AccountsYouTubeContent.Visibility = youTubeSelected ? Visibility.Visible : Visibility.Collapsed;
        AccountsTikTokContent.Visibility = tikTokSelected ? Visibility.Visible : Visibility.Collapsed;
        AccountsInstagramContent.Visibility = instagramSelected ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetYouTubeAccountStatus(string text)
    {
        YouTubeAccountStatusTextBlock.Text = text;
        if (YouTubeServiceChip is not null)
        {
            YouTubeServiceChip.Text = text;
        }

        UpdateYouTubeChipAppearance();
    }

    public void SetYouTubeConnectionState(bool isConnected)
    {
        _isYouTubeConnected = isConnected;
        UpdateYouTubeChipAppearance();
        SetTokenStatusText(isConnected);
        UpdateQuickActionStates(isConnected);
    }

    public void SetYouTubeAutoConnectState(bool isEnabled)
    {
        if (YouTubeAutoConnectToggle is null)
        {
            return;
        }

        _isAutoConnectBinding = true;
        YouTubeAutoConnectToggle.IsChecked = isEnabled;
        YouTubeAutoConnectToggle.Content = isEnabled
            ? LocalizationHelper.Get("Common.On")
            : LocalizationHelper.Get("Common.Off");
        if (YouTubeAutoConnectCheckBox is not null)
        {
            YouTubeAutoConnectCheckBox.IsChecked = isEnabled;
        }
        SetAutoConnectStatusText(isEnabled);
        _isAutoConnectBinding = false;
    }

    public void SetYouTubeLastSync(DateTime? utc)
    {
        if (YouTubeLastSyncTextBlock is null || YouTubeLastSyncDetailTextBlock is null)
        {
            return;
        }

        var displayText = utc.HasValue
            ? utc.Value.ToLocalTime().ToString("g")
            : LocalizationHelper.Get("Accounts.YouTube.LastSyncNever");

        YouTubeLastSyncTextBlock.Text = $"{LocalizationHelper.Get("Accounts.YouTube.LastSync")}: {displayText}";
        YouTubeLastSyncDetailTextBlock.Text = displayText;
    }

    public void SetYouTubePlaylists(IEnumerable<YouTubePlaylistInfo> playlists, string? selectedPlaylistId)
    {
        var playlistList = playlists?.ToList() ?? new List<YouTubePlaylistInfo>();
        YouTubePlaylistsListBox.ItemsSource = playlistList;

        if (!string.IsNullOrWhiteSpace(selectedPlaylistId))
        {
            var selected = playlistList.FirstOrDefault(p => string.Equals(p.Id, selectedPlaylistId, StringComparison.OrdinalIgnoreCase));
            YouTubePlaylistsListBox.SelectedItem = selected;
        }
        else
        {
            YouTubePlaylistsListBox.SelectedItem = null;
        }
    }

    public void ClearYouTubePlaylists()
    {
        YouTubePlaylistsListBox.ItemsSource = null;
    }

    public void SetYouTubeUploadStats(int uploadsLast7Days, int uploadsLast30Days, DateTimeOffset? lastSuccess)
    {
        if (YouTubeUploads7DaysTextBlock is null || YouTubeUploads30DaysTextBlock is null || YouTubeLastActionTextBlock is null)
        {
            return;
        }

        YouTubeUploads7DaysTextBlock.Text = uploadsLast7Days.ToString();
        YouTubeUploads30DaysTextBlock.Text = uploadsLast30Days.ToString();

        YouTubeLastActionTextBlock.Text = lastSuccess.HasValue
            ? lastSuccess.Value.ToLocalTime().ToString("g")
            : LocalizationHelper.Get("Accounts.Stats.EmptyDate");
    }

    public void SetYouTubeDefaultVisibility(VideoVisibility visibility)
    {
        if (YouTubeDefaultVisibilityComboBox is null)
        {
            return;
        }

        _isYouTubeSettingsBinding = true;
        SelectComboBoxItemByTag(YouTubeDefaultVisibilityComboBox, visibility);
        _isYouTubeSettingsBinding = false;
    }

    public VideoVisibility GetYouTubeDefaultVisibility()
    {
        if (YouTubeDefaultVisibilityComboBox?.SelectedItem is ComboBoxItem item &&
            item.Tag is VideoVisibility visibility)
        {
            return visibility;
        }

        return VideoVisibility.Unlisted;
    }

    public void SetYouTubeDefaultPublishTime(string? timeText)
    {
        if (YouTubeDefaultPublishTimeTextBox is null)
        {
            return;
        }

        _isYouTubeSettingsBinding = true;
        YouTubeDefaultPublishTimeTextBox.Text = timeText ?? string.Empty;
        _isYouTubeSettingsBinding = false;
    }

    public string GetYouTubeDefaultPublishTime() =>
        YouTubeDefaultPublishTimeTextBox?.Text ?? string.Empty;

    public void SetYouTubeLocale(string? localeKey)
    {
        if (YouTubeLocaleTextBlock is null)
        {
            return;
        }

        YouTubeLocaleTextBlock.Text = string.IsNullOrWhiteSpace(localeKey)
            ? LocalizationHelper.Get("Accounts.YouTube.Settings.LocaleAuto")
            : localeKey;
    }


    private void AccountsServiceTab_Checked(object sender, RoutedEventArgs e) =>
        AccountsServiceTabChecked?.Invoke(sender, e);

    private void YouTubeConnectButton_Click(object sender, RoutedEventArgs e) =>
        YouTubeConnectButtonClicked?.Invoke(sender, e);

    private void YouTubeDisconnectButton_Click(object sender, RoutedEventArgs e) =>
        YouTubeDisconnectButtonClicked?.Invoke(sender, e);

    private void YouTubePlaylistsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        YouTubePlaylistsSelectionChanged?.Invoke(sender, e);

    private void YouTubeDefaultVisibilityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isYouTubeSettingsBinding)
        {
            return;
        }

        YouTubeDefaultVisibilitySelectionChanged?.Invoke(sender, e);
    }

    private void YouTubeDefaultPublishTimeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isYouTubeSettingsBinding)
        {
            return;
        }

        YouTubeDefaultPublishTimeTextChanged?.Invoke(sender, e);
    }

    private void YouTubeAutoConnectCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_isAutoConnectBinding)
        {
            return;
        }

        _isAutoConnectBinding = true;
        YouTubeAutoConnectToggle.IsChecked = true;
        YouTubeAutoConnectToggle.Content = LocalizationHelper.Get("Common.On");
        SetAutoConnectStatusText(true);
        _isAutoConnectBinding = false;
        YouTubeAutoConnectToggled?.Invoke(this, true);
    }

    private void YouTubeAutoConnectCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isAutoConnectBinding)
        {
            return;
        }

        _isAutoConnectBinding = true;
        YouTubeAutoConnectToggle.IsChecked = false;
        YouTubeAutoConnectToggle.Content = LocalizationHelper.Get("Common.Off");
        SetAutoConnectStatusText(false);
        _isAutoConnectBinding = false;
        YouTubeAutoConnectToggled?.Invoke(this, false);
    }

    private void YouTubeAutoConnectToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_isAutoConnectBinding)
        {
            return;
        }

        if (YouTubeAutoConnectCheckBox is not null)
        {
            YouTubeAutoConnectCheckBox.IsChecked = true;
        }
        YouTubeAutoConnectToggle.Content = LocalizationHelper.Get("Common.On");
        SetAutoConnectStatusText(true);
        YouTubeAutoConnectToggled?.Invoke(this, true);
    }

    private void YouTubeAutoConnectToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isAutoConnectBinding)
        {
            return;
        }

        if (YouTubeAutoConnectCheckBox is not null)
        {
            YouTubeAutoConnectCheckBox.IsChecked = false;
        }
        YouTubeAutoConnectToggle.Content = LocalizationHelper.Get("Common.Off");
        SetAutoConnectStatusText(false);
        YouTubeAutoConnectToggled?.Invoke(this, false);
    }

    private void YouTubeRefreshButton_Click(object sender, RoutedEventArgs e) =>
        YouTubeRefreshButtonClicked?.Invoke(sender, e);

    private void YouTubeOpenLogsButton_Click(object sender, RoutedEventArgs e) =>
        YouTubeOpenLogsButtonClicked?.Invoke(sender, e);

    private void UpdateYouTubeChipAppearance()
    {
        if (YouTubeServiceChipBorder is null || YouTubeServiceChip is null)
        {
            return;
        }

        if (_isYouTubeConnected)
        {
            YouTubeServiceChipBorder.Background = new SolidColorBrush(Color.FromArgb(0x26, 0x4C, 0xAF, 0x50));
            YouTubeServiceChipBorder.BorderBrush = (Brush)FindResource("SuccessBrush");
            YouTubeServiceChip.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            YouTubeServiceChipBorder.Background = (Brush)FindResource("SurfaceHoverBrush");
            YouTubeServiceChipBorder.BorderBrush = (Brush)FindResource("BorderBrush");
            YouTubeServiceChip.Foreground = (Brush)FindResource("TextMutedBrush");
        }
    }

    private void SetAutoConnectStatusText(bool isEnabled)
    {
        if (YouTubeAutoConnectStatusTextBlock is null)
        {
            return;
        }

        YouTubeAutoConnectStatusTextBlock.Text = isEnabled
            ? LocalizationHelper.Get("Common.On")
            : LocalizationHelper.Get("Common.Off");
    }

    private void SetTokenStatusText(bool isConnected)
    {
        if (YouTubeTokenStatusTextBlock is null)
        {
            return;
        }

        YouTubeTokenStatusTextBlock.Text = isConnected
            ? LocalizationHelper.Get("Accounts.YouTube.Status.TokenConnected")
            : LocalizationHelper.Get("Accounts.YouTube.Status.TokenDisconnected");
    }

    private void UpdateQuickActionStates(bool isConnected)
    {
        if (YouTubeRefreshButton is not null)
        {
            YouTubeRefreshButton.IsEnabled = isConnected;
        }

        if (YouTubeDisconnectButton is not null)
        {
            YouTubeDisconnectButton.IsEnabled = isConnected;
        }

        if (YouTubeLogsButton is not null)
        {
            YouTubeLogsButton.IsEnabled = true;
        }
    }

    private static void SelectComboBoxItemByTag<T>(ComboBox comboBox, T value)
    {
        if (comboBox is null)
        {
            return;
        }

        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem comboItem && comboItem.Tag is T tag &&
                EqualityComparer<T>.Default.Equals(tag, value))
            {
                comboBox.SelectedItem = comboItem;
                return;
            }
        }
    }
}
