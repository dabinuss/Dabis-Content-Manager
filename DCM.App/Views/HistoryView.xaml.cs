using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using DCM.Core.Models;

namespace DCM.App.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    public event SelectionChangedEventHandler? HistoryFilterChanged;
    public event RoutedEventHandler? HistoryClearButtonClicked;
    public event MouseButtonEventHandler? HistoryDataGridMouseDoubleClick;
    public event RequestNavigateEventHandler? OpenUrlInBrowserRequested;

    public UploadHistoryEntry? SelectedEntry => HistoryDataGrid.SelectedItem as UploadHistoryEntry;

    public PlatformType? GetSelectedPlatformFilter()
    {
        if (HistoryPlatformFilterComboBox.SelectedItem is ComboBoxItem item && item.Tag is PlatformType platform)
        {
            return platform;
        }

        return null;
    }

    public string? GetSelectedStatusFilter()
    {
        if (HistoryStatusFilterComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag;
        }

        return null;
    }

    public void SetHistoryItems(IEnumerable<UploadHistoryEntry> entries)
    {
        var data = entries?.ToList() ?? new List<UploadHistoryEntry>();
        HistoryDataGrid.ItemsSource = data;
    }

    public void ClearHistoryItems()
    {
        HistoryDataGrid.ItemsSource = null;
    }

    private void HistoryFilter_Changed(object sender, SelectionChangedEventArgs e) =>
        HistoryFilterChanged?.Invoke(sender, e);

    private void HistoryClearButton_Click(object sender, RoutedEventArgs e) =>
        HistoryClearButtonClicked?.Invoke(sender, e);

    private void HistoryDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        HistoryDataGridMouseDoubleClick?.Invoke(sender, e);

    private void OpenUrlInBrowser(object sender, RequestNavigateEventArgs e) =>
        OpenUrlInBrowserRequested?.Invoke(sender, e);
}
