using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DCM.YouTube;

namespace DCM.App.Views;

public partial class AccountsView : UserControl
{
    public AccountsView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? AccountsServiceTabChecked;
    public event RoutedEventHandler? YouTubeConnectButtonClicked;
    public event RoutedEventHandler? YouTubeDisconnectButtonClicked;
    public event SelectionChangedEventHandler? YouTubePlaylistsSelectionChanged;

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

    private void AccountsServiceTab_Checked(object sender, RoutedEventArgs e) =>
        AccountsServiceTabChecked?.Invoke(sender, e);

    private void YouTubeConnectButton_Click(object sender, RoutedEventArgs e) =>
        YouTubeConnectButtonClicked?.Invoke(sender, e);

    private void YouTubeDisconnectButton_Click(object sender, RoutedEventArgs e) =>
        YouTubeDisconnectButtonClicked?.Invoke(sender, e);

    private void YouTubePlaylistsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        YouTubePlaylistsSelectionChanged?.Invoke(sender, e);
}
