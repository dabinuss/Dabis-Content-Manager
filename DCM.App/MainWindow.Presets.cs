using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using DCM.Core.Models;

namespace DCM.App;

public partial class MainWindow
{
    #region Presets

    private async Task LoadPresetsAsync()
    {
        try
        {
            var presets = await Task.Run(() => _presetRepository.Load().ToList());

            _loadedPresets.Clear();
            _loadedPresets.AddRange(presets);

            var defaultPreset = GetDefaultPreset(PlatformType.YouTube);

            PresetsPageView?.BindPresets(_loadedPresets, defaultPreset);
            LoadPresetIntoEditor(defaultPreset);

            UploadView.PresetComboBox.ItemsSource = _loadedPresets;
            if (defaultPreset is not null)
            {
                UploadView.PresetComboBox.SelectedItem = defaultPreset;
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Presets.LoadFailed", ex.Message);
        }
    }

    private UploadPreset? GetDefaultPreset(PlatformType platform) =>
        _loadedPresets.FirstOrDefault(p => p.IsDefault && p.Platform == platform)
        ?? _loadedPresets.FirstOrDefault(p => p.Platform == platform);

    private UploadPreset? FindPresetById(string? presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
        {
            return null;
        }

        return _loadedPresets.FirstOrDefault(p =>
            string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase));
    }

    private void PresetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetsPageView?.SelectedPreset is UploadPreset preset)
        {
            LoadPresetIntoEditor(preset);
        }
        else
        {
            LoadPresetIntoEditor(null);
        }
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPresetBinding)
        {
            return;
        }

        _presetState.ClearPreset();

        if (_activeDraft is null)
        {
            return;
        }

        if (UploadView.PresetComboBox.SelectedItem is UploadPreset preset)
        {
            _activeDraft.PresetId = preset.Id;
        }
        else
        {
            _activeDraft.PresetId = null;
        }

        ScheduleDraftPersistence();
    }

    private void PresetNewButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var preset = new UploadPreset
        {
            Name = LocalizationHelper.Get("Presets.DefaultName"),
            Platform = PlatformType.YouTube,
            IsDefault = !_loadedPresets.Any(p => p.Platform == PlatformType.YouTube && p.IsDefault),
            Visibility = _settings.DefaultVisibility,
            DescriptionTemplate = string.Empty
        };

        _loadedPresets.Add(preset);
        RefreshPresetBindings();

        PresetsPageView?.SelectPreset(preset);
        LoadPresetIntoEditor(preset);

        StatusTextBlock.Text = LocalizationHelper.Get("Status.Preset.Created");
    }

    private void PresetEditButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (PresetsPageView?.SelectedPreset is UploadPreset preset)
        {
            LoadPresetIntoEditor(preset);
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Preset.Editing", preset.Name);
        }
        else
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Preset.EditNone");
        }
    }

    private void PresetDeleteButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (PresetsPageView?.SelectedPreset is not UploadPreset preset)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Preset.DeleteNone");
            return;
        }

        var wasDefault = preset.IsDefault;
        var platform = preset.Platform;

        if (_loadedPresets.Remove(preset))
        {
            var nextPreset = _loadedPresets.FirstOrDefault(p => p.Platform == platform)
                             ?? _loadedPresets.FirstOrDefault();
            if (_currentEditingPreset?.Id == preset.Id)
            {
                _currentEditingPreset = nextPreset;
                LoadPresetIntoEditor(nextPreset);
            }
            if (_presetState.Matches(preset))
            {
                _presetState.Reset();
            }

            if (wasDefault)
            {
                EnsureDefaultPreset(platform, preferredPresetId: nextPreset?.Id);
            }

            SavePresetsToRepository();
            RefreshPresetBindings();
            PresetsPageView?.SelectPreset(nextPreset);
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Preset.Deleted", preset.Name);
            PresetsPageView?.ResetDirtyState();
        }
    }

    private void PresetDefaultToggleRequested(object? sender, UploadPreset preset)
    {
        if (preset is null)
        {
            return;
        }

        if (preset.IsDefault)
        {
            return;
        }

        var platform = preset.Platform;
        preset.IsDefault = true;
        ClearDefaultForPlatform(platform, preset.Id);

        SavePresetsToRepository();
        RefreshPresetBindings();
        PresetsPageView?.SelectPreset(preset);
        LoadPresetIntoEditor(preset);
        StatusTextBlock.Text = LocalizationHelper.Format("Status.Preset.Saved", preset.Name);
        PresetsPageView?.ResetDirtyState();
    }

    private void PresetSaveButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            if (_currentEditingPreset is null)
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Preset.NoneToSave");
                return;
            }

            var editorState = PresetsPageView?.TryGetEditorState();
            if (editorState is null)
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Preset.EditorNotReady");
                return;
            }

            var name = editorState.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Preset.NameRequired");
                return;
            }

            var platform = editorState.Platform;
            var isDefault = editorState.IsDefault;

            var duplicate = _loadedPresets
                .FirstOrDefault(p =>
                    p.Platform == platform &&
                    !string.Equals(p.Id, _currentEditingPreset.Id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (duplicate is not null)
            {
                StatusTextBlock.Text =
                    LocalizationHelper.Format("Status.Preset.Duplicate", platform);
                return;
            }

            _currentEditingPreset.Name = name;
            _currentEditingPreset.Platform = platform;
            _currentEditingPreset.IsDefault = isDefault;
            _currentEditingPreset.Description = string.IsNullOrWhiteSpace(editorState.Description)
                ? null
                : editorState.Description;
            _currentEditingPreset.TitlePrefix = editorState.TitlePrefix;
            _currentEditingPreset.TagsCsv = editorState.TagsCsv;
            _currentEditingPreset.Visibility = editorState.Visibility;
            _currentEditingPreset.PlaylistId = editorState.PlaylistId;
            _currentEditingPreset.PlaylistTitle = editorState.PlaylistTitle;
            _currentEditingPreset.CategoryId = editorState.CategoryId;
            _currentEditingPreset.Language = editorState.Language;
            _currentEditingPreset.MadeForKids = editorState.MadeForKids;
            _currentEditingPreset.CommentStatus = editorState.CommentStatus;
            _currentEditingPreset.DescriptionTemplate = editorState.DescriptionTemplate;

            if (isDefault)
            {
                ClearDefaultForPlatform(platform, _currentEditingPreset.Id);
            }
            else
            {
                EnsureDefaultPreset(platform, preferredPresetId: _currentEditingPreset.Id);
            }

            SavePresetsToRepository();
            RefreshPresetBindings();

            PresetsPageView?.BindPresets(_loadedPresets, _currentEditingPreset);

            if (UploadView.PresetComboBox is not null)
            {
                UploadView.PresetComboBox.SelectedItem = _currentEditingPreset;
            }

            StatusTextBlock.Text = LocalizationHelper.Format("Status.Preset.Saved", _currentEditingPreset.Name);
            PresetsPageView?.ResetDirtyState();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Preset.SaveFailed", ex.Message);
        }
    }

    private void LoadPresetIntoEditor(UploadPreset? preset)
    {
        _currentEditingPreset = preset;
        PresetsPageView?.PopulateEditor(preset);
    }

    private void SavePresetsToRepository()
    {
        try
        {
            _presetRepository.Save(_loadedPresets);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Presets.SaveFailed", ex.Message);
        }
    }

    private void RefreshPresetBindings()
    {
        _isPresetBinding = true;
        PresetsPageView?.BindPresets(_loadedPresets, _currentEditingPreset);

        UploadView.PresetComboBox.ItemsSource = null;
        UploadView.PresetComboBox.ItemsSource = _loadedPresets;
        if (_currentEditingPreset is not null && _loadedPresets.Contains(_currentEditingPreset))
        {
            UploadView.PresetComboBox.SelectedItem = _currentEditingPreset;
        }
        else if (_loadedPresets.Count > 0 && UploadView.PresetComboBox.SelectedItem is null)
        {
            UploadView.PresetComboBox.SelectedIndex = 0;
        }
        _isPresetBinding = false;
    }

    private void ClearDefaultForPlatform(PlatformType platform, string keepPresetId)
    {
        foreach (var other in _loadedPresets.Where(p =>
                     p.Platform == platform && !string.Equals(p.Id, keepPresetId, StringComparison.OrdinalIgnoreCase)))
        {
            other.IsDefault = false;
        }
    }

    private bool EnsureDefaultPreset(PlatformType platform, string? preferredPresetId)
    {
        if (_loadedPresets.Any(p => p.Platform == platform && p.IsDefault))
        {
            return true;
        }

        var fallback = _loadedPresets.FirstOrDefault(p =>
            p.Platform == platform &&
            (preferredPresetId is null || !string.Equals(p.Id, preferredPresetId, StringComparison.OrdinalIgnoreCase)));

        if (fallback is null)
        {
            return false;
        }

        fallback.IsDefault = true;
        return true;
    }

    #endregion
}
