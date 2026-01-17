using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DCM.App.Models;
using DCM.App;
using DCM.Core.Models;
using DCM.YouTube;

namespace DCM.App.Views;

public partial class PresetsView : UserControl
{
    private bool _isEditorBinding;
    private bool _isDirty;

    public PresetsView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? PresetNewButtonClicked;
    public event RoutedEventHandler? PresetEditButtonClicked;
    public event RoutedEventHandler? PresetDeleteButtonClicked;
    public event RoutedEventHandler? PresetSaveButtonClicked;
    public event SelectionChangedEventHandler? PresetListBoxSelectionChanged;
    public event EventHandler<UploadPreset>? PresetDefaultToggleRequested;
    public event EventHandler<bool>? PresetDirtyStateChanged;

    public UploadPreset? SelectedPreset => PresetListBox.SelectedItem as UploadPreset;
    public bool IsDirty => _isDirty;

    public void BindPresets(IEnumerable<UploadPreset> presets, UploadPreset? selectedPreset)
    {
        var ordered = presets
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PresetListBox.ItemsSource = null;
        PresetListBox.ItemsSource = ordered;
        PresetListBox.SelectedItem = selectedPreset;
    }

    public void SelectPreset(UploadPreset? preset)
    {
        PresetListBox.SelectedItem = preset;
    }

    public void PopulateEditor(UploadPreset? preset)
    {
        _isEditorBinding = true;
        if (preset is null)
        {
            PresetNameTextBox.Text = string.Empty;
            PresetPlatformComboBox.SelectedItem = null;
            PresetDescriptionTextBox.Text = string.Empty;
            PresetTitlePrefixTextBox.Text = string.Empty;
            PresetTagsTextBox.Text = string.Empty;
            PresetPlaylistComboBox.SelectedItem = null;
            PresetCategoryComboBox.SelectedItem = null;
            PresetLanguageComboBox.SelectedItem = null;
            PresetDescriptionTemplateTextBox.Text = string.Empty;
            SelectComboBoxItemByTag(PresetVisibilityComboBox, VideoVisibility.Unlisted);
            SelectComboBoxItemByTag(PresetMadeForKidsComboBox, MadeForKidsSetting.Default);
            SelectComboBoxItemByTag(PresetCommentStatusComboBox, CommentStatusSetting.Default);
            _isEditorBinding = false;
            ResetDirtyState();
            return;
        }

        PresetNameTextBox.Text = preset.Name;
        PresetPlatformComboBox.SelectedItem = preset.Platform;
        PresetDescriptionTextBox.Text = preset.Description ?? string.Empty;
        PresetTitlePrefixTextBox.Text = preset.TitlePrefix ?? string.Empty;
        PresetTagsTextBox.Text = preset.TagsCsv ?? string.Empty;
        SelectPlaylistById(preset.PlaylistId, preset.PlaylistTitle);
        SelectCategoryById(preset.CategoryId);
        SelectLanguageByCode(preset.Language);
        PresetDescriptionTemplateTextBox.Text = preset.DescriptionTemplate ?? string.Empty;
        SelectComboBoxItemByTag(PresetVisibilityComboBox, preset.Visibility);
        SelectComboBoxItemByTag(PresetMadeForKidsComboBox, preset.MadeForKids);
        SelectComboBoxItemByTag(PresetCommentStatusComboBox, preset.CommentStatus);
        _isEditorBinding = false;
        ResetDirtyState();
    }

    public void SetPlatformOptions(Array platforms)
    {
        PresetPlatformComboBox.ItemsSource = platforms;
    }

    public void SetPlaylistOptions(IEnumerable<YouTubePlaylistInfo> playlists)
    {
        var list = playlists?.ToList() ?? new List<YouTubePlaylistInfo>();
        list.Insert(0, new YouTubePlaylistInfo(string.Empty, LocalizationHelper.Get("Presets.Option.None")));
        PresetPlaylistComboBox.ItemsSource = list;

        if (SelectedPreset is not null)
        {
            SelectPlaylistById(SelectedPreset.PlaylistId, SelectedPreset.PlaylistTitle);
        }
    }

    public void SetCategoryOptions(IEnumerable<CategoryOption> categories)
    {
        var list = categories?.ToList() ?? new List<CategoryOption>();
        list.Insert(0, new CategoryOption(string.Empty, LocalizationHelper.Get("Presets.Option.None")));
        PresetCategoryComboBox.ItemsSource = list;

        if (SelectedPreset is not null)
        {
            SelectCategoryById(SelectedPreset.CategoryId);
        }
    }

    public void SetLanguageOptions(IEnumerable<LanguageOption> languages)
    {
        var list = languages?.ToList() ?? new List<LanguageOption>();
        list.Insert(0, new LanguageOption(string.Empty, LocalizationHelper.Get("Presets.Option.None")));
        PresetLanguageComboBox.ItemsSource = list;

        if (SelectedPreset is not null)
        {
            SelectLanguageByCode(SelectedPreset.Language);
        }
    }

    public void SetPlaceholders(IEnumerable<string> placeholders) =>
        PlaceholderItemsControl.ItemsSource = placeholders?.ToList() ?? new List<string>();

    public void ResetDirtyState()
    {
        SetDirty(false);
    }

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
            PresetPlaylistComboBox is null ||
            PresetCategoryComboBox is null ||
            PresetLanguageComboBox is null ||
            PresetMadeForKidsComboBox is null ||
            PresetCommentStatusComboBox is null ||
            PresetDescriptionTemplateTextBox is null)
        {
            return null;
        }

        var name = (PresetNameTextBox.Text ?? string.Empty).Trim();
        var platform = PresetPlatformComboBox.SelectedItem is PlatformType selectedPlatform
            ? selectedPlatform
            : PlatformType.YouTube;
        var isDefault = SelectedPreset?.IsDefault ?? false;
        var description = PresetDescriptionTextBox.Text;
        var titlePrefix = PresetTitlePrefixTextBox.Text ?? string.Empty;
        var tagsCsv = PresetTagsTextBox.Text ?? string.Empty;
        var visibility = GetSelectedVisibility();
        string? playlistId = null;
        string? playlistTitle = null;
        var hasPlaylists = PresetPlaylistComboBox.Items
            .OfType<YouTubePlaylistInfo>()
            .Any(p => !string.IsNullOrWhiteSpace(p.Id));

        if (PresetPlaylistComboBox.SelectedItem is YouTubePlaylistInfo playlist &&
            !string.IsNullOrWhiteSpace(playlist.Id))
        {
            playlistId = playlist.Id;
            playlistTitle = playlist.Title;
        }
        else if (!hasPlaylists && SelectedPreset is not null && PresetPlaylistComboBox.SelectedItem is null)
        {
            playlistId = SelectedPreset.PlaylistId;
            playlistTitle = SelectedPreset.PlaylistTitle;
        }
        var hasCategories = PresetCategoryComboBox.Items
            .OfType<CategoryOption>()
            .Any(c => !string.IsNullOrWhiteSpace(c.Id));

        var categoryId = (PresetCategoryComboBox.SelectedItem as CategoryOption)?.Id;
        if (string.IsNullOrWhiteSpace(categoryId) && !hasCategories && SelectedPreset is not null &&
            PresetCategoryComboBox.SelectedItem is null)
        {
            categoryId = SelectedPreset.CategoryId;
        }
        var hasLanguages = PresetLanguageComboBox.Items
            .OfType<LanguageOption>()
            .Any(l => !string.IsNullOrWhiteSpace(l.Code));

        var language = (PresetLanguageComboBox.SelectedItem as LanguageOption)?.Code;
        if (string.IsNullOrWhiteSpace(language) && !hasLanguages && SelectedPreset is not null &&
            PresetLanguageComboBox.SelectedItem is null)
        {
            language = SelectedPreset.Language;
        }
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

    private void SelectPlaylistById(string? playlistId, string? playlistTitle)
    {
        if (PresetPlaylistComboBox is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(playlistId) && string.IsNullOrWhiteSpace(playlistTitle))
        {
            PresetPlaylistComboBox.SelectedItem = PresetPlaylistComboBox.Items
                .OfType<YouTubePlaylistInfo>()
                .FirstOrDefault(p => string.IsNullOrWhiteSpace(p.Id));
            return;
        }

        foreach (var item in PresetPlaylistComboBox.Items)
        {
            if (item is YouTubePlaylistInfo playlist)
            {
                if (!string.IsNullOrWhiteSpace(playlistId) &&
                    string.Equals(playlist.Id, playlistId, StringComparison.OrdinalIgnoreCase))
                {
                    PresetPlaylistComboBox.SelectedItem = playlist;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(playlistTitle) &&
                    string.Equals(playlist.Title, playlistTitle, StringComparison.OrdinalIgnoreCase))
                {
                    PresetPlaylistComboBox.SelectedItem = playlist;
                    return;
                }
            }
        }

        PresetPlaylistComboBox.SelectedItem = null;
    }

    private void SelectCategoryById(string? categoryId)
    {
        if (PresetCategoryComboBox is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(categoryId))
        {
            PresetCategoryComboBox.SelectedItem = PresetCategoryComboBox.Items
                .OfType<CategoryOption>()
                .FirstOrDefault(c => string.IsNullOrWhiteSpace(c.Id));
            return;
        }

        foreach (var item in PresetCategoryComboBox.Items)
        {
            if (item is CategoryOption category &&
                string.Equals(category.Id, categoryId, StringComparison.OrdinalIgnoreCase))
            {
                PresetCategoryComboBox.SelectedItem = category;
                return;
            }
        }

        PresetCategoryComboBox.SelectedItem = null;
    }

    private void SelectLanguageByCode(string? languageCode)
    {
        if (PresetLanguageComboBox is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(languageCode))
        {
            PresetLanguageComboBox.SelectedItem = PresetLanguageComboBox.Items
                .OfType<LanguageOption>()
                .FirstOrDefault(l => string.IsNullOrWhiteSpace(l.Code));
            return;
        }

        foreach (var item in PresetLanguageComboBox.Items)
        {
            if (item is LanguageOption language &&
                string.Equals(language.Code, languageCode, StringComparison.OrdinalIgnoreCase))
            {
                PresetLanguageComboBox.SelectedItem = language;
                return;
            }
        }

        PresetLanguageComboBox.SelectedItem = null;
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

    private void PresetDefaultToggleButton_Click(object sender, RoutedEventArgs e)
    {
        HandlePresetDefaultToggle(sender);
        e.Handled = true;
    }

    private void PresetDefaultToggleButton_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        HandlePresetDefaultToggle(sender);
        e.Handled = true;
    }

    private void HandlePresetDefaultToggle(object sender)
    {
        if (sender is not Button button)
        {
            return;
        }

        var preset = button.CommandParameter as UploadPreset
                     ?? button.DataContext as UploadPreset;
        if (preset is null)
        {
            return;
        }

        PresetListBox.SelectedItem = preset;
        PresetDefaultToggleRequested?.Invoke(this, preset);
    }

    private void PresetEditorField_Changed(object sender, RoutedEventArgs e)
    {
        if (_isEditorBinding)
        {
            return;
        }

        UpdateDirtyState();
    }

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
        UpdateDirtyState();
    }

    private void UpdateDirtyState()
    {
        if (SelectedPreset is null)
        {
            SetDirty(false);
            return;
        }

        var editorState = TryGetEditorState();
        if (editorState is null)
        {
            SetDirty(false);
            return;
        }

        var preset = SelectedPreset;

        var isDirty =
            !string.Equals(editorState.Name, preset.Name, StringComparison.Ordinal) ||
            editorState.Platform != preset.Platform ||
            editorState.IsDefault != preset.IsDefault ||
            !string.Equals(editorState.Description ?? string.Empty, preset.Description ?? string.Empty, StringComparison.Ordinal) ||
            !string.Equals(editorState.TitlePrefix, preset.TitlePrefix ?? string.Empty, StringComparison.Ordinal) ||
            !string.Equals(editorState.TagsCsv, preset.TagsCsv ?? string.Empty, StringComparison.Ordinal) ||
            editorState.Visibility != preset.Visibility ||
            !string.Equals(editorState.PlaylistId ?? string.Empty, preset.PlaylistId ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(editorState.PlaylistTitle ?? string.Empty, preset.PlaylistTitle ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(editorState.CategoryId ?? string.Empty, preset.CategoryId ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(editorState.Language ?? string.Empty, preset.Language ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
            editorState.MadeForKids != preset.MadeForKids ||
            editorState.CommentStatus != preset.CommentStatus ||
            !string.Equals(editorState.DescriptionTemplate, preset.DescriptionTemplate ?? string.Empty, StringComparison.Ordinal);

        SetDirty(isDirty);
    }

    private void SetDirty(bool isDirty)
    {
        if (_isDirty == isDirty)
        {
            return;
        }

        _isDirty = isDirty;
        PresetDirtyBadge.Visibility = _isDirty ? Visibility.Visible : Visibility.Collapsed;
        PresetDirtyStateChanged?.Invoke(this, _isDirty);
    }
}
