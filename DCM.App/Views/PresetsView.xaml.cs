using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DCM.Core.Models;

namespace DCM.App.Views;

public partial class PresetsView : UserControl
{
    public PresetsView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? PresetNewButtonClicked;
    public event RoutedEventHandler? PresetEditButtonClicked;
    public event RoutedEventHandler? PresetDeleteButtonClicked;
    public event RoutedEventHandler? PresetSaveButtonClicked;
    public event SelectionChangedEventHandler? PresetListBoxSelectionChanged;

    public UploadPreset? SelectedPreset => PresetListBox.SelectedItem as UploadPreset;

    public void BindPresets(IEnumerable<UploadPreset> presets, UploadPreset? selectedPreset)
    {
        PresetListBox.ItemsSource = null;
        PresetListBox.ItemsSource = presets;
        PresetListBox.SelectedItem = selectedPreset;
    }

    public void SelectPreset(UploadPreset? preset)
    {
        PresetListBox.SelectedItem = preset;
    }

    public void PopulateEditor(UploadPreset? preset)
    {
        if (preset is null)
        {
            PresetNameTextBox.Text = string.Empty;
            PresetPlatformComboBox.SelectedItem = null;
            PresetIsDefaultCheckBox.IsChecked = false;
            PresetDescriptionTextBox.Text = string.Empty;
            PresetTitlePrefixTextBox.Text = string.Empty;
            PresetTagsTextBox.Text = string.Empty;
            PresetPlaylistIdTextBox.Text = string.Empty;
            PresetPlaylistTitleTextBox.Text = string.Empty;
            PresetCategoryIdTextBox.Text = string.Empty;
            PresetLanguageTextBox.Text = string.Empty;
            PresetDescriptionTemplateTextBox.Text = string.Empty;
            SelectComboBoxItemByTag(PresetVisibilityComboBox, VideoVisibility.Unlisted);
            SelectComboBoxItemByTag(PresetMadeForKidsComboBox, MadeForKidsSetting.Default);
            SelectComboBoxItemByTag(PresetCommentStatusComboBox, CommentStatusSetting.Default);
            return;
        }

        PresetNameTextBox.Text = preset.Name;
        PresetPlatformComboBox.SelectedItem = preset.Platform;
        PresetIsDefaultCheckBox.IsChecked = preset.IsDefault;
        PresetDescriptionTextBox.Text = preset.Description ?? string.Empty;
        PresetTitlePrefixTextBox.Text = preset.TitlePrefix ?? string.Empty;
        PresetTagsTextBox.Text = preset.TagsCsv ?? string.Empty;
        PresetPlaylistIdTextBox.Text = preset.PlaylistId ?? string.Empty;
        PresetPlaylistTitleTextBox.Text = preset.PlaylistTitle ?? string.Empty;
        PresetCategoryIdTextBox.Text = preset.CategoryId ?? string.Empty;
        PresetLanguageTextBox.Text = preset.Language ?? string.Empty;
        PresetDescriptionTemplateTextBox.Text = preset.DescriptionTemplate ?? string.Empty;
        SelectComboBoxItemByTag(PresetVisibilityComboBox, preset.Visibility);
        SelectComboBoxItemByTag(PresetMadeForKidsComboBox, preset.MadeForKids);
        SelectComboBoxItemByTag(PresetCommentStatusComboBox, preset.CommentStatus);
    }

    public void SetPlatformOptions(Array platforms)
    {
        PresetPlatformComboBox.ItemsSource = platforms;
    }

    public void SetPlaceholders(IEnumerable<string> placeholders) =>
        PlaceholderItemsControl.ItemsSource = placeholders?.ToList() ?? new List<string>();

    public record PresetEditorState(
        string Name,
        PlatformType Platform,
        bool IsDefault,
        string? Description,
        string TitlePrefix,
        string TagsCsv,
        VideoVisibility Visibility,
        string? PlaylistId,
        string? PlaylistTitle,
        string? CategoryId,
        string? Language,
        MadeForKidsSetting MadeForKids,
        CommentStatusSetting CommentStatus,
        string DescriptionTemplate);

    public PresetEditorState? TryGetEditorState()
    {
        if (PresetNameTextBox is null ||
            PresetPlatformComboBox is null ||
            PresetDescriptionTextBox is null ||
            PresetTitlePrefixTextBox is null ||
            PresetTagsTextBox is null ||
            PresetVisibilityComboBox is null ||
            PresetPlaylistIdTextBox is null ||
            PresetPlaylistTitleTextBox is null ||
            PresetCategoryIdTextBox is null ||
            PresetLanguageTextBox is null ||
            PresetMadeForKidsComboBox is null ||
            PresetCommentStatusComboBox is null ||
            PresetDescriptionTemplateTextBox is null ||
            PresetIsDefaultCheckBox is null)
        {
            return null;
        }

        var name = (PresetNameTextBox.Text ?? string.Empty).Trim();
        var platform = PresetPlatformComboBox.SelectedItem is PlatformType selectedPlatform
            ? selectedPlatform
            : PlatformType.YouTube;
        var isDefault = PresetIsDefaultCheckBox.IsChecked == true;
        var description = PresetDescriptionTextBox.Text;
        var titlePrefix = PresetTitlePrefixTextBox.Text ?? string.Empty;
        var tagsCsv = PresetTagsTextBox.Text ?? string.Empty;
        var visibility = GetSelectedVisibility();
        var playlistId = PresetPlaylistIdTextBox.Text;
        var playlistTitle = PresetPlaylistTitleTextBox.Text;
        var categoryId = PresetCategoryIdTextBox.Text;
        var language = PresetLanguageTextBox.Text;
        var madeForKids = GetSelectedMadeForKids();
        var commentStatus = GetSelectedCommentStatus();
        var descriptionTemplate = PresetDescriptionTemplateTextBox.Text ?? string.Empty;

        return new PresetEditorState(
            name,
            platform,
            isDefault,
            description,
            titlePrefix,
            tagsCsv,
            visibility,
            string.IsNullOrWhiteSpace(playlistId) ? null : playlistId.Trim(),
            string.IsNullOrWhiteSpace(playlistTitle) ? null : playlistTitle.Trim(),
            string.IsNullOrWhiteSpace(categoryId) ? null : categoryId.Trim(),
            string.IsNullOrWhiteSpace(language) ? null : language.Trim(),
            madeForKids,
            commentStatus,
            descriptionTemplate);
    }

    private VideoVisibility GetSelectedVisibility()
    {
        if (PresetVisibilityComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is VideoVisibility visibility)
        {
            return visibility;
        }

        return VideoVisibility.Unlisted;
    }

    private MadeForKidsSetting GetSelectedMadeForKids()
    {
        if (PresetMadeForKidsComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is MadeForKidsSetting setting)
        {
            return setting;
        }

        return MadeForKidsSetting.Default;
    }

    private CommentStatusSetting GetSelectedCommentStatus()
    {
        if (PresetCommentStatusComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is CommentStatusSetting setting)
        {
            return setting;
        }

        return CommentStatusSetting.Default;
    }

    private static void SelectComboBoxItemByTag(ComboBox comboBox, object? tag)
    {
        if (comboBox is null)
        {
            return;
        }

        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem comboItem &&
                comboItem.Tag is not null &&
                comboItem.Tag.Equals(tag))
            {
                comboBox.SelectedItem = comboItem;
                return;
            }
        }

        comboBox.SelectedItem = null;
    }

    private void PresetNewButton_Click(object sender, RoutedEventArgs e) =>
        PresetNewButtonClicked?.Invoke(sender, e);

    private void PresetEditButton_Click(object sender, RoutedEventArgs e) =>
        PresetEditButtonClicked?.Invoke(sender, e);

    private void PresetDeleteButton_Click(object sender, RoutedEventArgs e) =>
        PresetDeleteButtonClicked?.Invoke(sender, e);

    private void PresetSaveButton_Click(object sender, RoutedEventArgs e) =>
        PresetSaveButtonClicked?.Invoke(sender, e);

    private void PresetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        PresetListBoxSelectionChanged?.Invoke(sender, e);

    private void PlaceholderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Content is not string placeholder)
        {
            return;
        }

        if (PresetDescriptionTemplateTextBox is null)
        {
            return;
        }

        var tb = PresetDescriptionTemplateTextBox;
        tb.Focus();
        var caret = tb.CaretIndex;
        var text = tb.Text ?? string.Empty;
        tb.Text = text.Insert(caret, placeholder);
        tb.CaretIndex = caret + placeholder.Length;
    }
}
