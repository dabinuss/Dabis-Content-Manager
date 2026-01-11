using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Effects;
using DCM.Core.Models;

namespace DCM.App.Views;

public partial class UploadView : UserControl
{
    public UploadView()
    {
        InitializeComponent();
    }

    public event DragEventHandler? VideoDropDragOver;
    public event DragEventHandler? VideoDropDragLeave;
    public event DragEventHandler? VideoDropDrop;
    public event MouseButtonEventHandler? VideoDropZoneClicked;

    public event RoutedEventHandler? VideoChangeButtonClicked;
    public event RoutedEventHandler? UploadButtonClicked;
    public event TextChangedEventHandler? TitleTextBoxTextChanged;
    public event TextChangedEventHandler? DescriptionTextBoxTextChanged;
    public event RoutedEventHandler? GenerateTitleButtonClicked;
    public event RoutedEventHandler? GenerateDescriptionButtonClicked;
    public event SelectionChangedEventHandler? TemplateComboBoxSelectionChanged;
    public event RoutedEventHandler? ApplyTemplateButtonClicked;
    public event RoutedEventHandler? GenerateTagsButtonClicked;
    public event RoutedEventHandler? TranscribeButtonClicked;
    public event RoutedEventHandler? TranscriptionExportButtonClicked;
    public event TextChangedEventHandler? TagsTextBoxTextChanged;
    public event TextChangedEventHandler? TranscriptTextBoxTextChanged;
    public event SelectionChangedEventHandler? UploadItemsSelectionChanged;
    public event RoutedEventHandler? AddVideosButtonClicked;
    public event RoutedEventHandler? UploadAllButtonClicked;
    public event RoutedEventHandler? TranscribeAllButtonClicked;
    public event RoutedEventHandler? RemoveDraftButtonClicked;
    public event RoutedEventHandler? CancelUploadButtonClicked;
    public event RoutedEventHandler? TranscriptionPrioritizeButtonClicked;
    public event RoutedEventHandler? TranscriptionSkipButtonClicked;
    public event EventHandler<string>? SuggestionItemClicked;
    public event EventHandler? SuggestionCloseButtonClicked;

    public event DragEventHandler? ThumbnailDropDragOver;
    public event DragEventHandler? ThumbnailDropDragLeave;
    public event DragEventHandler? ThumbnailDropDrop;
    public event MouseButtonEventHandler? ThumbnailDropZoneClicked;
    public event RoutedEventHandler? ThumbnailClearButtonClicked;

    public event MouseButtonEventHandler? FocusTargetOnContainerClicked;
    public event RoutedEventHandler? PlatformYouTubeToggleChecked;
    public event RoutedEventHandler? PlatformYouTubeToggleUnchecked;
    public event SelectionChangedEventHandler? VisibilitySelectionChanged;
    public event SelectionChangedEventHandler? PlaylistSelectionChanged;
    public event RoutedEventHandler? ScheduleCheckBoxChecked;
    public event RoutedEventHandler? ScheduleCheckBoxUnchecked;
    public event SelectionChangedEventHandler? ScheduleDateChanged;
    public event TextChangedEventHandler? ScheduleTimeTextChanged;

    private void VideoDrop_DragOver(object sender, DragEventArgs e) =>
        VideoDropDragOver?.Invoke(sender, e);

    private void VideoDrop_DragLeave(object sender, DragEventArgs e) =>
        VideoDropDragLeave?.Invoke(sender, e);

    private void VideoDrop_Drop(object sender, DragEventArgs e) =>
        VideoDropDrop?.Invoke(sender, e);

    private void VideoDropZone_Click(object sender, MouseButtonEventArgs e) =>
        VideoDropZoneClicked?.Invoke(sender, e);

    private void VideoChangeButton_Click(object sender, RoutedEventArgs e) =>
        VideoChangeButtonClicked?.Invoke(sender, e);

    private void UploadButton_Click(object sender, RoutedEventArgs e) =>
        UploadButtonClicked?.Invoke(sender, e);

    private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        TitleTextBoxTextChanged?.Invoke(sender, e);

    private void DescriptionTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        DescriptionTextBoxTextChanged?.Invoke(sender, e);

    private void GenerateTitleButton_Click(object sender, RoutedEventArgs e) =>
        GenerateTitleButtonClicked?.Invoke(sender, e);

    private void GenerateDescriptionButton_Click(object sender, RoutedEventArgs e) =>
        GenerateDescriptionButtonClicked?.Invoke(sender, e);

    private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        TemplateComboBoxSelectionChanged?.Invoke(sender, e);

    private void ApplyTemplateButton_Click(object sender, RoutedEventArgs e) =>
        ApplyTemplateButtonClicked?.Invoke(sender, e);

    private void GenerateTagsButton_Click(object sender, RoutedEventArgs e) =>
        GenerateTagsButtonClicked?.Invoke(sender, e);

    private void TranscribeButton_Click(object sender, RoutedEventArgs e) =>
        TranscribeButtonClicked?.Invoke(sender, e);

    private void TranscriptionExportButton_Click(object sender, RoutedEventArgs e) =>
        TranscriptionExportButtonClicked?.Invoke(sender, e);

    private void TagsTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        TagsTextBoxTextChanged?.Invoke(sender, e);

    private void TranscriptTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        TranscriptTextBoxTextChanged?.Invoke(sender, e);

    private void UploadItemsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UploadItemsSelectionChanged?.Invoke(sender, e);

    private void AddVideosButton_Click(object sender, RoutedEventArgs e) =>
        AddVideosButtonClicked?.Invoke(sender, e);

    private void UploadAllButton_Click(object sender, RoutedEventArgs e) =>
        UploadAllButtonClicked?.Invoke(sender, e);

    private void TranscribeAllButton_Click(object sender, RoutedEventArgs e) =>
        TranscribeAllButtonClicked?.Invoke(sender, e);

    private void RemoveDraftButton_Click(object sender, RoutedEventArgs e) =>
        RemoveDraftButtonClicked?.Invoke(sender, e);

    private void CancelDraftUploadButton_Click(object sender, RoutedEventArgs e) =>
        CancelUploadButtonClicked?.Invoke(sender, e);

    private void TranscriptionQueuePrioritizeButton_Click(object sender, RoutedEventArgs e) =>
        TranscriptionPrioritizeButtonClicked?.Invoke(sender, e);

    private void TranscriptionQueueSkipButton_Click(object sender, RoutedEventArgs e) =>
        TranscriptionSkipButtonClicked?.Invoke(sender, e);

    private void ThumbnailDrop_DragOver(object sender, DragEventArgs e) =>
        ThumbnailDropDragOver?.Invoke(sender, e);

    private void ThumbnailDrop_DragLeave(object sender, DragEventArgs e) =>
        ThumbnailDropDragLeave?.Invoke(sender, e);

    private void ThumbnailDrop_Drop(object sender, DragEventArgs e) =>
        ThumbnailDropDrop?.Invoke(sender, e);

    private void ThumbnailDropZone_Click(object sender, MouseButtonEventArgs e) =>
        ThumbnailDropZoneClicked?.Invoke(sender, e);

    private void ThumbnailClearButton_Click(object sender, RoutedEventArgs e) =>
        ThumbnailClearButtonClicked?.Invoke(sender, e);

    private void FocusTargetOnContainerClick(object sender, MouseButtonEventArgs e) =>
        FocusTargetOnContainerClicked?.Invoke(sender, e);

    private void PlatformYouTubeToggle_Checked(object sender, RoutedEventArgs e) =>
        PlatformYouTubeToggleChecked?.Invoke(sender, e);

    private void PlatformYouTubeToggle_Unchecked(object sender, RoutedEventArgs e) =>
        PlatformYouTubeToggleUnchecked?.Invoke(sender, e);

    private void VisibilityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        VisibilitySelectionChanged?.Invoke(sender, e);

    private void PlaylistComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        PlaylistSelectionChanged?.Invoke(sender, e);

    private void ScheduleCheckBox_Checked(object sender, RoutedEventArgs e) =>
        ScheduleCheckBoxChecked?.Invoke(sender, e);

    private void ScheduleCheckBox_Unchecked(object sender, RoutedEventArgs e) =>
        ScheduleCheckBoxUnchecked?.Invoke(sender, e);

    private void ScheduleDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e) =>
        ScheduleDateChanged?.Invoke(sender, e);

    private void ScheduleTimeTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ScheduleTimeTextChanged?.Invoke(sender, e);

    private void SuggestionItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is string suggestion)
        {
            SuggestionItemClicked?.Invoke(this, suggestion);
        }
    }

    private void SuggestionCloseButton_Click(object sender, RoutedEventArgs e) =>
        SuggestionCloseButtonClicked?.Invoke(this, e);

    public void ShowSuggestionOverlay(string title, IEnumerable<string> suggestions)
    {
        SuggestionTitleTextBlock.Text = title;
        SuggestionItemsControl.ItemsSource = suggestions?.ToList() ?? new List<string>();
        SuggestionOverlay.Visibility = Visibility.Visible;
        SetContentBlur(isEnabled: true);
    }

    public void HideSuggestionOverlay()
    {
        SuggestionOverlay.Visibility = Visibility.Collapsed;
        SuggestionItemsControl.ItemsSource = null;
        SuggestionTitleTextBlock.Text = string.Empty;
        SetContentBlur(isEnabled: false);
    }

    public void SetUploadItemsSource(IEnumerable? source)
    {
        UploadItemsListBox.ItemsSource = source;
    }

    public object? GetSelectedUploadItem() => UploadItemsListBox.SelectedItem;

    public void SetSelectedUploadItem(object? item)
    {
        UploadItemsListBox.SelectedItem = item;
    }

    public void SetDefaultVisibility(VideoVisibility visibility)
    {
        if (VisibilityComboBox is null)
        {
            return;
        }

        foreach (var item in VisibilityComboBox.Items)
        {
            if (item is ComboBoxItem comboItem &&
                comboItem.Tag is VideoVisibility tag &&
                tag == visibility)
            {
                VisibilityComboBox.SelectedItem = comboItem;
                return;
            }
        }
    }

    private void SetContentBlur(bool isEnabled)
    {
        var effect = isEnabled ? new BlurEffect { Radius = 3 } : null;
        HeaderPanel.Effect = effect;
        VideoDropZone.Effect = effect;
        MainContentGrid.Effect = effect;
    }
}
