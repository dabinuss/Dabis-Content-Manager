using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    public event RoutedEventHandler? GenerateTitleButtonClicked;
    public event RoutedEventHandler? GenerateDescriptionButtonClicked;
    public event SelectionChangedEventHandler? TemplateComboBoxSelectionChanged;
    public event RoutedEventHandler? ApplyTemplateButtonClicked;
    public event RoutedEventHandler? GenerateTagsButtonClicked;
    public event RoutedEventHandler? TranscribeButtonClicked;
    public event RoutedEventHandler? TranscriptionExportButtonClicked;

    public event DragEventHandler? ThumbnailDropDragOver;
    public event DragEventHandler? ThumbnailDropDragLeave;
    public event DragEventHandler? ThumbnailDropDrop;
    public event MouseButtonEventHandler? ThumbnailDropZoneClicked;
    public event RoutedEventHandler? ThumbnailClearButtonClicked;

    public event MouseButtonEventHandler? FocusTargetOnContainerClicked;

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
}
