using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DCM.App;
using DCM.App.Infrastructure;
using DCM.App.Models;
using DCM.Core.Models;

namespace DCM.App.Views;

public partial class UploadView : UserControl
{
    private bool _suppressBringIntoView;
    private readonly UiDispatcher _ui;
    private readonly ObservableCollection<string> _tagChips = new();
    private bool _isSyncingTagsText;
    private bool _isSyncingTagChips;
    private bool _isSyncingTagInput;
    private bool _isSyncingScheduleTime;

    public UploadView()
    {
        InitializeComponent();
        _ui = new UiDispatcher(Dispatcher);
        TagsChipItemsControl.ItemsSource = _tagChips;
        InitializeScheduleTimeSelectors();
    }

    public ComboBox CategoryComboBoxControl => CategoryComboBox;
    public ComboBox LanguageComboBoxControl => LanguageComboBox;

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
    public event SelectionChangedEventHandler? PresetComboBoxSelectionChanged;
    public event RoutedEventHandler? ApplyPresetButtonClicked;
    public event RoutedEventHandler? GenerateTagsButtonClicked;
    public event RoutedEventHandler? GenerateChaptersButtonClicked;
    public event RoutedEventHandler? TranscribeButtonClicked;
    public event RoutedEventHandler? TranscriptionExportButtonClicked;
    public event TextChangedEventHandler? TagsTextBoxTextChanged;
    public event TextChangedEventHandler? TranscriptTextBoxTextChanged;
    public event TextChangedEventHandler? ChaptersTextBoxTextChanged;
    public event SelectionChangedEventHandler? UploadItemsSelectionChanged;
    public event RoutedEventHandler? AddVideosButtonClicked;
    public event RoutedEventHandler? UploadAllButtonClicked;
    public event RoutedEventHandler? TranscribeAllButtonClicked;
    public event RoutedEventHandler? RemoveDraftButtonClicked;
    public event RoutedEventHandler? CancelUploadButtonClicked;
    public event RoutedEventHandler? FastFillSuggestionsButtonClicked;
    public event RoutedEventHandler? UploadDraftButtonClicked;
    public event RoutedEventHandler? TranscribeDraftButtonClicked;
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
    public event SelectionChangedEventHandler? CategorySelectionChanged;
    public event SelectionChangedEventHandler? LanguageSelectionChanged;
    public event SelectionChangedEventHandler? MadeForKidsSelectionChanged;
    public event SelectionChangedEventHandler? CommentStatusSelectionChanged;
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

    private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        TitleTextBoxTextChanged?.Invoke(sender, e);
        UpdateTitleValidation();
    }

    private void UpdateTitleValidation()
    {
        if (TitleValidationIcon == null || TitleValidationIconGlyph == null)
            return;

        var text = TitleTextBox.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(text))
        {
            TitleValidationIcon.Visibility = Visibility.Collapsed;
            return;
        }

        TitleValidationIcon.Visibility = Visibility.Visible;

        if (text.Length >= 3)
        {
            // Valid: green checkmark
            TitleValidationIconGlyph.Text = "\ue5ca";
            TitleValidationIconGlyph.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            // Invalid: red X
            TitleValidationIconGlyph.Text = "\ue5cd";
            TitleValidationIconGlyph.Foreground = (Brush)FindResource("DangerBrush");
        }
    }

    private void DescriptionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        DescriptionTextBoxTextChanged?.Invoke(sender, e);
        UpdateDescriptionValidation();
    }

    private void UpdateDescriptionValidation()
    {
        if (DescriptionValidationIcon == null || DescriptionValidationIconGlyph == null)
            return;

        var text = DescriptionTextBox.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(text))
        {
            DescriptionValidationIcon.Visibility = Visibility.Collapsed;
            return;
        }

        DescriptionValidationIcon.Visibility = Visibility.Visible;

        if (text.Length >= 10)
        {
            DescriptionValidationIconGlyph.Text = "\ue5ca";
            DescriptionValidationIconGlyph.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            DescriptionValidationIconGlyph.Text = "\ue5cd";
            DescriptionValidationIconGlyph.Foreground = (Brush)FindResource("DangerBrush");
        }
    }

    private void GenerateTitleButton_Click(object sender, RoutedEventArgs e) =>
        GenerateTitleButtonClicked?.Invoke(sender, e);

    private void GenerateDescriptionButton_Click(object sender, RoutedEventArgs e) =>
        GenerateDescriptionButtonClicked?.Invoke(sender, e);

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        PresetComboBoxSelectionChanged?.Invoke(sender, e);

    private void ApplyPresetButton_Click(object sender, RoutedEventArgs e) =>
        ApplyPresetButtonClicked?.Invoke(sender, e);

    private void GenerateTagsButton_Click(object sender, RoutedEventArgs e) =>
        GenerateTagsButtonClicked?.Invoke(sender, e);

    private void GenerateChaptersButton_Click(object sender, RoutedEventArgs e) =>
        GenerateChaptersButtonClicked?.Invoke(sender, e);

    private void TranscribeButton_Click(object sender, RoutedEventArgs e) =>
        TranscribeButtonClicked?.Invoke(sender, e);

    private void TranscriptionExportButton_Click(object sender, RoutedEventArgs e) =>
        TranscriptionExportButtonClicked?.Invoke(sender, e);

    private void TagsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isSyncingTagsText)
        {
            SyncTagChipsFromTextBox();
        }

        TagsTextBoxTextChanged?.Invoke(sender, e);
        UpdateTagsValidation();
    }

    private void UpdateTagsValidation()
    {
        if (TagsValidationIcon == null || TagsValidationIconGlyph == null)
            return;

        var text = TagsTextBox.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(text))
        {
            TagsValidationIcon.Visibility = Visibility.Collapsed;
            return;
        }

        TagsValidationIcon.Visibility = Visibility.Visible;

        // Valid if at least one tag exists
        if (text.Length >= 2)
        {
            TagsValidationIconGlyph.Text = "\ue5ca";
            TagsValidationIconGlyph.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            TagsValidationIconGlyph.Text = "\ue5cd";
            TagsValidationIconGlyph.Foreground = (Brush)FindResource("DangerBrush");
        }
    }

    private void TranscriptTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        TranscriptTextBoxTextChanged?.Invoke(sender, e);
        UpdateTranscriptionValidation();
    }

    private void UpdateTranscriptionValidation()
    {
        if (TranscriptionValidationIcon == null || TranscriptionValidationIconGlyph == null)
            return;

        var text = TranscriptTextBox.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(text))
        {
            TranscriptionValidationIcon.Visibility = Visibility.Collapsed;
            return;
        }

        TranscriptionValidationIcon.Visibility = Visibility.Visible;

        // Valid if transcript exists
        if (text.Length >= 20)
        {
            TranscriptionValidationIconGlyph.Text = "\ue5ca";
            TranscriptionValidationIconGlyph.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            TranscriptionValidationIconGlyph.Text = "\ue5cd";
            TranscriptionValidationIconGlyph.Foreground = (Brush)FindResource("DangerBrush");
        }
    }

    private void ChaptersTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ChaptersTextBoxTextChanged?.Invoke(sender, e);

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

    private void FastFillSuggestionsButton_Click(object sender, RoutedEventArgs e) =>
        FastFillSuggestionsButtonClicked?.Invoke(sender, e);

    private void UploadDraftButton_Click(object sender, RoutedEventArgs e) =>
        UploadDraftButtonClicked?.Invoke(sender, e);

    private void TranscribeDraftButton_Click(object sender, RoutedEventArgs e) =>
        TranscribeDraftButtonClicked?.Invoke(sender, e);

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

    private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        CategorySelectionChanged?.Invoke(sender, e);

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        LanguageSelectionChanged?.Invoke(sender, e);

    public void SetCategoryOptions(IEnumerable<CategoryOption> categories)
    {
        var list = categories?.ToList() ?? new List<CategoryOption>();
        list.Insert(0, new CategoryOption(string.Empty, LocalizationHelper.Get("Presets.Option.None")));
        CategoryComboBox.ItemsSource = new ObservableCollection<CategoryOption>(list);
    }

    public string? GetSelectedCategoryId()
    {
        var id = (CategoryComboBox.SelectedItem as CategoryOption)?.Id;
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    public void SelectCategoryById(string? categoryId)
    {
        if (CategoryComboBox is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(categoryId))
        {
            CategoryComboBox.SelectedItem = CategoryComboBox.Items
                .OfType<CategoryOption>()
                .FirstOrDefault(c => string.IsNullOrWhiteSpace(c.Id));
            return;
        }

        var list = CategoryComboBox.ItemsSource as IList<CategoryOption>;
        if (list is not null)
        {
            var match = list.FirstOrDefault(item =>
                string.Equals(item.Id, categoryId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                CategoryComboBox.SelectedItem = match;
                return;
            }

            var fallback = new CategoryOption(categoryId, categoryId);
            list.Add(fallback);
            CategoryComboBox.SelectedItem = fallback;
            return;
        }

        foreach (var item in CategoryComboBox.Items)
        {
            if (item is CategoryOption category &&
                string.Equals(category.Id, categoryId, StringComparison.OrdinalIgnoreCase))
            {
                CategoryComboBox.SelectedItem = category;
                return;
            }
        }

        CategoryComboBox.SelectedItem = null;
    }

    public void SetLanguageOptions(IEnumerable<LanguageOption> languages)
    {
        var list = languages?.ToList() ?? new List<LanguageOption>();
        list.Insert(0, new LanguageOption(string.Empty, LocalizationHelper.Get("Presets.Option.None")));
        LanguageComboBox.ItemsSource = new ObservableCollection<LanguageOption>(list);
    }

    public string? GetSelectedLanguageCode()
    {
        var code = (LanguageComboBox.SelectedItem as LanguageOption)?.Code;
        return string.IsNullOrWhiteSpace(code) ? null : code;
    }

    public void SelectLanguageByCode(string? languageCode)
    {
        if (LanguageComboBox is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(languageCode))
        {
            LanguageComboBox.SelectedItem = null;
            return;
        }

        var list = LanguageComboBox.ItemsSource as IList<LanguageOption>;
        if (list is not null)
        {
            var match = list.FirstOrDefault(item =>
                string.Equals(item.Code, languageCode, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                LanguageComboBox.SelectedItem = match;
                return;
            }

            var fallback = new LanguageOption(languageCode, languageCode);
            list.Add(fallback);
            LanguageComboBox.SelectedItem = fallback;
            return;
        }

        foreach (var item in LanguageComboBox.Items)
        {
            if (item is LanguageOption language &&
                string.Equals(language.Code, languageCode, StringComparison.OrdinalIgnoreCase))
            {
                LanguageComboBox.SelectedItem = language;
                return;
            }
        }

        LanguageComboBox.SelectedItem = null;
    }

    private void MadeForKidsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        MadeForKidsSelectionChanged?.Invoke(sender, e);

    private void CommentStatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        CommentStatusSelectionChanged?.Invoke(sender, e);

    private void ScheduleCheckBox_Checked(object sender, RoutedEventArgs e) =>
        ScheduleCheckBoxChecked?.Invoke(sender, e);

    private void ScheduleCheckBox_Unchecked(object sender, RoutedEventArgs e) =>
        ScheduleCheckBoxUnchecked?.Invoke(sender, e);

    private void ScheduleDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        ClampScheduleDateToToday();
        ScheduleDateChanged?.Invoke(sender, e);
    }

    private void ScheduleTodayLink_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;

        if (ScheduleDatePicker is not null)
        {
            ScheduleDatePicker.SelectedDate = today;
        }

        if (ScheduleCalendar is not null)
        {
            ScheduleCalendar.SelectedDate = today;
            ScheduleCalendar.DisplayDate = today;
        }

        ClampScheduleToNow();
        UpdateTimeDisplays();
        UpdateScheduleTimeTextFromSelectors();
    }

    private void ScheduleTimeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SyncScheduleSelectorsFromTextBox();
        ScheduleTimeTextChanged?.Invoke(sender, e);
    }

    private const int ScheduleTimeRowCount = 7;
    private const int ScheduleTimeCenterIndex = ScheduleTimeRowCount / 2;
    private const int ScheduleMinuteStep = 5;

    private int _selectedHour;
    private int _selectedMinute;

    private void InitializeScheduleTimeSelectors()
    {
        // Set default to now + 1 hour
        var defaultTime = DateTime.Now.AddHours(1);
        SetScheduleDateBlackoutPast();

        if (ScheduleDatePicker is not null)
        {
            ScheduleDatePicker.SelectedDate = defaultTime.Date;
        }

        if (ScheduleCalendar is not null)
        {
            ScheduleCalendar.SelectedDate = defaultTime.Date;
            ScheduleCalendar.DisplayDate = defaultTime.Date;
        }

        _selectedHour = defaultTime.Hour;
        _selectedMinute = NormalizeMinute(defaultTime.Minute);
        UpdateTimeDisplays();

        _isSyncingScheduleTime = true;
        if (ScheduleTimeTextBox is not null)
        {
            ScheduleTimeTextBox.Text = $"{_selectedHour:00}:{_selectedMinute:00}";
        }
        _isSyncingScheduleTime = false;
    }

    private void UpdateTimeDisplays()
    {
        UpdateScheduleTimeRows();
    }

    private void UpdateScheduleTimeTextFromSelectors()
    {
        if (_isSyncingScheduleTime || ScheduleTimeTextBox is null)
        {
            return;
        }

        _isSyncingScheduleTime = true;
        ScheduleTimeTextBox.Text = $"{_selectedHour:00}:{_selectedMinute:00}";
        _isSyncingScheduleTime = false;
    }

    private void HourUpButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedHour = (_selectedHour + 1) % 24;
        ClampScheduleToNow();
        UpdateTimeDisplays();
        UpdateScheduleTimeTextFromSelectors();
    }

    private void HourDownButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedHour = (_selectedHour - 1 + 24) % 24;
        ClampScheduleToNow();
        UpdateTimeDisplays();
        UpdateScheduleTimeTextFromSelectors();
    }

    private void MinuteUpButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedMinute = (_selectedMinute + ScheduleMinuteStep) % 60;
        ClampScheduleToNow();
        UpdateTimeDisplays();
        UpdateScheduleTimeTextFromSelectors();
    }

    private void MinuteDownButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedMinute = (_selectedMinute - ScheduleMinuteStep + 60) % 60;
        ClampScheduleToNow();
        UpdateTimeDisplays();
        UpdateScheduleTimeTextFromSelectors();
    }

    private void SyncScheduleSelectorsFromTextBox()
    {
        if (_isSyncingScheduleTime || ScheduleTimeTextBox is null)
        {
            return;
        }

        if (!TimeSpan.TryParse(ScheduleTimeTextBox.Text, out var time))
        {
            return;
        }

        _selectedHour = time.Hours;
        _selectedMinute = NormalizeMinute(time.Minutes);
        ClampScheduleToNow();
        UpdateTimeDisplays();
    }

    private void ScheduleNowLink_Click(object sender, RoutedEventArgs e)
    {
        var now = RoundUpToMinuteStep(DateTime.Now);
        _selectedHour = now.Hour;
        _selectedMinute = now.Minute;

        if (ScheduleDatePicker is not null)
        {
            ScheduleDatePicker.SelectedDate = now.Date;
        }

        if (ScheduleCalendar is not null)
        {
            ScheduleCalendar.SelectedDate = now.Date;
            ScheduleCalendar.DisplayDate = now.Date;
        }

        ClampScheduleToNow();
        UpdateTimeDisplays();
        UpdateScheduleTimeTextFromSelectors();
    }

    private void ScheduleHourRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (!int.TryParse(button.Content?.ToString(), out var value))
        {
            return;
        }

        _selectedHour = value;
        ClampScheduleToNow();
        UpdateTimeDisplays();
        UpdateScheduleTimeTextFromSelectors();
    }

    private void ScheduleMinuteRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (!int.TryParse(button.Content?.ToString(), out var value))
        {
            return;
        }

        _selectedMinute = NormalizeMinute(value);
        ClampScheduleToNow();
        UpdateTimeDisplays();
        UpdateScheduleTimeTextFromSelectors();
    }

    private void UpdateScheduleTimeRows()
    {
        UpdateScheduleTimeRows(
            new[]
            {
                ScheduleHourRow0,
                ScheduleHourRow1,
                ScheduleHourRow2,
                ScheduleHourRow3,
                ScheduleHourRow4,
                ScheduleHourRow5,
                ScheduleHourRow6
            },
            _selectedHour,
            24,
            1);

        UpdateScheduleTimeRows(
            new[]
            {
                ScheduleMinuteRow0,
                ScheduleMinuteRow1,
                ScheduleMinuteRow2,
                ScheduleMinuteRow3,
                ScheduleMinuteRow4,
                ScheduleMinuteRow5,
                ScheduleMinuteRow6
            },
            _selectedMinute,
            60,
            ScheduleMinuteStep);

        UpdateScheduleTimeRowEnabledStates();
    }

    private static void UpdateScheduleTimeRows(Button?[] buttons, int selectedValue, int modulus, int step)
    {
        for (var i = 0; i < ScheduleTimeRowCount; i++)
        {
            var button = buttons[i];
            if (button is null)
            {
                continue;
            }

            var offset = (i - ScheduleTimeCenterIndex) * step;
            var value = (selectedValue + offset) % modulus;
            if (value < 0)
            {
                value += modulus;
            }

            button.Content = value.ToString("00");
            button.Tag = i == ScheduleTimeCenterIndex ? "Selected" : null;
        }
    }

    private void UpdateScheduleTimeRowEnabledStates()
    {
        var selectedDate = ScheduleDatePicker?.SelectedDate?.Date;
        if (selectedDate is null)
        {
            SetButtonsEnabled(new[]
            {
                ScheduleHourRow0,
                ScheduleHourRow1,
                ScheduleHourRow2,
                ScheduleHourRow3,
                ScheduleHourRow4,
                ScheduleHourRow5,
                ScheduleHourRow6
            }, true);

            SetButtonsEnabled(new[]
            {
                ScheduleMinuteRow0,
                ScheduleMinuteRow1,
                ScheduleMinuteRow2,
                ScheduleMinuteRow3,
                ScheduleMinuteRow4,
                ScheduleMinuteRow5,
                ScheduleMinuteRow6
            }, true);
            return;
        }

        var minSchedule = RoundUpToMinuteStep(DateTime.Now);
        if (selectedDate > minSchedule.Date)
        {
            SetButtonsEnabled(new[]
            {
                ScheduleHourRow0,
                ScheduleHourRow1,
                ScheduleHourRow2,
                ScheduleHourRow3,
                ScheduleHourRow4,
                ScheduleHourRow5,
                ScheduleHourRow6
            }, true);

            SetButtonsEnabled(new[]
            {
                ScheduleMinuteRow0,
                ScheduleMinuteRow1,
                ScheduleMinuteRow2,
                ScheduleMinuteRow3,
                ScheduleMinuteRow4,
                ScheduleMinuteRow5,
                ScheduleMinuteRow6
            }, true);
            return;
        }

        var minHour = minSchedule.Hour;
        var minMinute = minSchedule.Minute;

        SetButtonsEnabled(new[]
        {
            ScheduleHourRow0,
            ScheduleHourRow1,
            ScheduleHourRow2,
            ScheduleHourRow3,
            ScheduleHourRow4,
            ScheduleHourRow5,
            ScheduleHourRow6
        }, button =>
        {
            if (!int.TryParse(button.Content?.ToString(), out var hour))
            {
                return false;
            }

            return hour > minHour || (hour == minHour && _selectedMinute >= minMinute);
        });

        SetButtonsEnabled(new[]
        {
            ScheduleMinuteRow0,
            ScheduleMinuteRow1,
            ScheduleMinuteRow2,
            ScheduleMinuteRow3,
            ScheduleMinuteRow4,
            ScheduleMinuteRow5,
            ScheduleMinuteRow6
        }, button =>
        {
            if (!int.TryParse(button.Content?.ToString(), out var minute))
            {
                return false;
            }

            if (_selectedHour > minHour)
            {
                return true;
            }

            if (_selectedHour < minHour)
            {
                return false;
            }

            return minute >= minMinute;
        });
    }

    private static void SetButtonsEnabled(Button?[] buttons, bool isEnabled)
    {
        foreach (var button in buttons)
        {
            if (button is null)
            {
                continue;
            }

            button.IsEnabled = isEnabled;
        }
    }

    private static void SetButtonsEnabled(Button?[] buttons, Func<Button, bool> isEnabled)
    {
        foreach (var button in buttons)
        {
            if (button is null)
            {
                continue;
            }

            button.IsEnabled = isEnabled(button);
        }
    }

    private static int NormalizeMinute(int minute)
    {
        if (minute < 0)
        {
            minute = 0;
        }

        minute %= 60;
        return (minute / ScheduleMinuteStep) * ScheduleMinuteStep;
    }

    private void ClampScheduleToNow()
    {
        var minSchedule = RoundUpToMinuteStep(DateTime.Now);
        var selectedDate = ScheduleDatePicker?.SelectedDate?.Date ?? minSchedule.Date;
        var selectedDateTime = new DateTime(
            selectedDate.Year,
            selectedDate.Month,
            selectedDate.Day,
            _selectedHour,
            _selectedMinute,
            0);

        if (selectedDateTime >= minSchedule)
        {
            return;
        }

        _selectedHour = minSchedule.Hour;
        _selectedMinute = minSchedule.Minute;

        if (ScheduleDatePicker is not null)
        {
            ScheduleDatePicker.SelectedDate = minSchedule.Date;
        }

        if (ScheduleCalendar is not null)
        {
            ScheduleCalendar.SelectedDate = minSchedule.Date;
            ScheduleCalendar.DisplayDate = minSchedule.Date;
        }
    }

    private static DateTime RoundUpToMinuteStep(DateTime time)
    {
        var roundedMinute = ((time.Minute + ScheduleMinuteStep - 1) / ScheduleMinuteStep) * ScheduleMinuteStep;
        if (roundedMinute >= 60)
        {
            time = time.AddHours(1);
            roundedMinute = 0;
        }

        return new DateTime(time.Year, time.Month, time.Day, time.Hour, roundedMinute, 0);
    }

    private void SetScheduleDateBlackoutPast()
    {
        var today = DateTime.Today;
        var blackoutEnd = today.AddDays(-1);
        if (blackoutEnd < DateTime.MinValue.AddDays(1))
        {
            return;
        }

        if (ScheduleDatePicker is not null)
        {
            ScheduleDatePicker.BlackoutDates.Clear();
            ScheduleDatePicker.BlackoutDates.Add(new CalendarDateRange(DateTime.MinValue, blackoutEnd));
        }

        if (ScheduleCalendar is not null)
        {
            ScheduleCalendar.BlackoutDates.Clear();
            ScheduleCalendar.BlackoutDates.Add(new CalendarDateRange(DateTime.MinValue, blackoutEnd));
        }
    }

    private void ClampScheduleDateToToday()
    {
        var today = DateTime.Today;
        var selectedDate = ScheduleDatePicker?.SelectedDate?.Date;
        if (selectedDate is null || selectedDate >= today)
        {
            return;
        }

        if (ScheduleDatePicker is not null)
        {
            ScheduleDatePicker.SelectedDate = today;
        }

        if (ScheduleCalendar is not null)
        {
            ScheduleCalendar.SelectedDate = today;
            ScheduleCalendar.DisplayDate = today;
        }
    }

    private void SuggestionItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is string suggestion)
        {
            SuggestionItemClicked?.Invoke(this, suggestion);
        }
    }

    private void SuggestionCloseButton_Click(object sender, RoutedEventArgs e) =>
        SuggestionCloseButtonClicked?.Invoke(this, e);

    public void SetTranscribeAllActionState(bool isCancel)
    {
        _ui.Run(() =>
        {
            if (TranscribeAllActionLinkLabel is null)
            {
                return;
            }

            TranscribeAllActionLinkLabel.Text = isCancel
                ? LocalizationHelper.Get("Upload.Actions.TranscribeAll.Cancel")
                : LocalizationHelper.Get("Upload.Actions.TranscribeAll");
        }, UiUpdatePolicy.StatusPriority);
    }

    public void SetTranscribeDraftActionState(bool isCancel)
    {
        _ui.Run(() =>
        {
            if (ActionTranscribeDraftLabel is null || ActionTranscribeDraftIcon is null || ActionTranscribeButton is null)
            {
                return;
            }

            if (isCancel)
            {
                ActionTranscribeDraftLabel.Text = LocalizationHelper.Get("Common.Cancel");
                ActionTranscribeDraftIcon.Text = char.ConvertFromUtf32(0xE5C9);
                ActionTranscribeButton.ToolTip = LocalizationHelper.Get("Upload.Tooltip.TranscribeDraft.Cancel");
            }
            else
            {
                ActionTranscribeDraftLabel.Text = LocalizationHelper.Get("Upload.Fields.Transcripe");
                ActionTranscribeDraftIcon.Text = char.ConvertFromUtf32(0xE048);
                ActionTranscribeButton.ToolTip = LocalizationHelper.Get("Upload.Tooltip.TranscribeVideo");
            }
        }, UiUpdatePolicy.StatusPriority);
    }

    public void ShowTitleSuggestionsLoading(string title, int expectedCount)
    {
        PrepareSuggestionPanel(
            TitleSuggestionsPanel,
            TitleSuggestionsLoadingPanel,
            TitleSuggestionItemsControl,
            TitleSuggestionsTargetTextBlock,
            title,
            expectedCount);
    }

    public void ShowTitleSuggestions(string title, IEnumerable<string> suggestions)
    {
        HideSuggestionPanels(except: TitleSuggestionsPanel);
        SetSuggestionItems(
            TitleSuggestionsPanel,
            TitleSuggestionsLoadingPanel,
            TitleSuggestionItemsControl,
            TitleSuggestionsTargetTextBlock,
            title,
            suggestions);
    }

    public void ShowDescriptionSuggestionsLoading(string title, int expectedCount)
    {
        PrepareSuggestionPanel(
            DescriptionSuggestionsPanel,
            DescriptionSuggestionsLoadingPanel,
            DescriptionSuggestionItemsControl,
            DescriptionSuggestionsTargetTextBlock,
            title,
            expectedCount);
    }

    public void ShowDescriptionSuggestions(string title, IEnumerable<string> suggestions)
    {
        HideSuggestionPanels(except: DescriptionSuggestionsPanel);
        SetSuggestionItems(
            DescriptionSuggestionsPanel,
            DescriptionSuggestionsLoadingPanel,
            DescriptionSuggestionItemsControl,
            DescriptionSuggestionsTargetTextBlock,
            title,
            suggestions);
    }

    public void ShowTagsSuggestionsLoading(string title, int expectedCount)
    {
        PrepareSuggestionPanel(
            TagsSuggestionsPanel,
            TagsSuggestionsLoadingPanel,
            TagsSuggestionItemsControl,
            TagsSuggestionsTargetTextBlock,
            title,
            expectedCount);
    }

    public void ShowTagsSuggestions(string title, IEnumerable<string> suggestions)
    {
        HideSuggestionPanels(except: TagsSuggestionsPanel);
        SetSuggestionItems(
            TagsSuggestionsPanel,
            TagsSuggestionsLoadingPanel,
            TagsSuggestionItemsControl,
            TagsSuggestionsTargetTextBlock,
            title,
            suggestions);
    }

    public void HideSuggestions()
    {
        HideSuggestionPanels(except: null);
        ClearSuggestionItems();
    }

    public FrameworkElement? GetTitleSuggestionAnchor() => TitleTextBox;

    public FrameworkElement? GetDescriptionSuggestionAnchor() => DescriptionTextBox;

    public FrameworkElement? GetTagsSuggestionAnchor() => TagInputTextBox;

    public void SetUploadItemsSource(IEnumerable? source)
    {
        UploadItemsListBox.ItemsSource = source;
    }

    public object? GetSelectedUploadItem() => UploadItemsListBox.SelectedItem;

    public void SetSelectedUploadItem(object? item)
    {
        UploadItemsListBox.SelectedItem = item;
    }

    public ScrollViewer? GetUploadContentScrollViewer() => UploadContentScrollViewer;

    public void SuppressUploadItemsBringIntoView(bool suppress)
    {
        _suppressBringIntoView = suppress;
    }

    public double GetUploadItemsVerticalOffset()
    {
        var viewer = FindDescendant<ScrollViewer>(UploadItemsListBox);
        return viewer?.VerticalOffset ?? 0;
    }

    public void RestoreUploadItemsVerticalOffset(double offset)
    {
        if (offset < 0)
        {
            return;
        }

        _ui.Run(() =>
        {
            var viewer = FindDescendant<ScrollViewer>(UploadItemsListBox);
            viewer?.ScrollToVerticalOffset(offset);
        }, UiUpdatePolicy.LayoutPriority);
    }

    private void UploadItemsListBox_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (_suppressBringIntoView)
        {
            e.Handled = true;
        }
    }

    private void TagsInputBorder_MouseDown(object sender, MouseButtonEventArgs e)
    {
        TagInputTextBox.Focus();
        TagInputTextBox.CaretIndex = TagInputTextBox.Text?.Length ?? 0;
    }

    private void TagInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.OemComma || e.Key == Key.OemSemicolon)
        {
            CommitTagInput();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back && string.IsNullOrWhiteSpace(TagInputTextBox.Text))
        {
            RemoveLastTagChip();
            e.Handled = true;
        }
    }

    private void TagInputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingTagInput)
        {
            return;
        }

        var text = TagInputTextBox.Text ?? string.Empty;
        if (!text.Contains(',') && !text.Contains(';'))
        {
            return;
        }

        var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.None);
        if (parts.Length == 0)
        {
            return;
        }

        var hasTrailingSeparator = text.EndsWith(",") || text.EndsWith(";");
        var toCommit = hasTrailingSeparator ? parts : parts.Take(parts.Length - 1);
        AddTagChips(toCommit);

        var remaining = hasTrailingSeparator ? string.Empty : parts.LastOrDefault() ?? string.Empty;
        _isSyncingTagInput = true;
        TagInputTextBox.Text = remaining.TrimStart();
        TagInputTextBox.CaretIndex = TagInputTextBox.Text.Length;
        _isSyncingTagInput = false;
    }

    private void TagInputTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitTagInput();
    }

    private void TagChipRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag)
        {
            RemoveTagChip(tag);
        }
    }

    private void CommitTagInput()
    {
        if (string.IsNullOrWhiteSpace(TagInputTextBox.Text))
        {
            return;
        }

        var input = TagInputTextBox.Text ?? string.Empty;
        AddTagChips(input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
        TagInputTextBox.Text = string.Empty;
    }

    private void SyncTagChipsFromTextBox()
    {
        if (_isSyncingTagChips)
        {
            return;
        }

        _isSyncingTagChips = true;
        var tags = ParseTagsCsv(TagsTextBox.Text);
        _tagChips.Clear();
        foreach (var tag in tags)
        {
            _tagChips.Add(tag);
        }
        TagInputTextBox.Text = string.Empty;
        _isSyncingTagChips = false;
    }

    private void AddTagChips(IEnumerable<string> tags)
    {
        var existing = new HashSet<string>(_tagChips, StringComparer.OrdinalIgnoreCase);
        var addedAny = false;

        foreach (var raw in tags)
        {
            var tag = NormalizeTag(raw);
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            if (existing.Add(tag))
            {
                _tagChips.Add(tag);
                addedAny = true;
            }
        }

        if (addedAny)
        {
            UpdateTagsTextBox();
        }
    }

    private void RemoveTagChip(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        var index = _tagChips
            .Select((value, i) => new { value, i })
            .FirstOrDefault(item => string.Equals(item.value, tag, StringComparison.OrdinalIgnoreCase))
            ?.i ?? -1;

        if (index >= 0)
        {
            _tagChips.RemoveAt(index);
            UpdateTagsTextBox();
        }
    }

    private void RemoveLastTagChip()
    {
        if (_tagChips.Count == 0)
        {
            return;
        }

        _tagChips.RemoveAt(_tagChips.Count - 1);
        UpdateTagsTextBox();
    }

    private void UpdateTagsTextBox()
    {
        _isSyncingTagsText = true;
        TagsTextBox.Text = string.Join(", ", _tagChips);
        _isSyncingTagsText = false;
    }

    private static IEnumerable<string> ParseTagsCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Array.Empty<string>();
        }

        return csv
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeTag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeTag(string raw) => raw.Trim();

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is T match)
            {
                return match;
            }

            var count = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < count; i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
        }

        return null;
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

    private void HideSuggestionPanels(Border? except)
    {
        if (TitleSuggestionsPanel != except)
        {
            TitleSuggestionsPanel.Visibility = Visibility.Collapsed;
            TitleSuggestionsTargetTextBlock.Text = string.Empty;
            TitleSuggestionsPanel.MinHeight = 0;
            TitleSuggestionsLoadingPanel.Visibility = Visibility.Collapsed;
            TitleSuggestionItemsControl.Visibility = Visibility.Collapsed;
        }

        if (DescriptionSuggestionsPanel != except)
        {
            DescriptionSuggestionsPanel.Visibility = Visibility.Collapsed;
            DescriptionSuggestionsTargetTextBlock.Text = string.Empty;
            DescriptionSuggestionsPanel.MinHeight = 0;
            DescriptionSuggestionsLoadingPanel.Visibility = Visibility.Collapsed;
            DescriptionSuggestionItemsControl.Visibility = Visibility.Collapsed;
        }

        if (TagsSuggestionsPanel != except)
        {
            TagsSuggestionsPanel.Visibility = Visibility.Collapsed;
            TagsSuggestionsTargetTextBlock.Text = string.Empty;
            TagsSuggestionsPanel.MinHeight = 0;
            TagsSuggestionsLoadingPanel.Visibility = Visibility.Collapsed;
            TagsSuggestionItemsControl.Visibility = Visibility.Collapsed;
        }
    }

    public void SetMadeForKids(MadeForKidsSetting setting) =>
        SelectComboBoxItemByTag(MadeForKidsComboBox, setting);

    public void SetCommentStatus(CommentStatusSetting setting) =>
        SelectComboBoxItemByTag(CommentStatusComboBox, setting);

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

    private void ClearSuggestionItems()
    {
        TitleSuggestionItemsControl.ItemsSource = null;
        DescriptionSuggestionItemsControl.ItemsSource = null;
        TagsSuggestionItemsControl.ItemsSource = null;
    }

    private void PrepareSuggestionPanel(
        Border panel,
        FrameworkElement loadingPanel,
        ItemsControl itemsControl,
        TextBlock targetLabel,
        string title,
        int expectedCount)
    {
        HideSuggestionPanels(except: panel);
        panel.MinHeight = CalculateSuggestionPanelMinHeight(expectedCount);
        targetLabel.Text = title;
        loadingPanel.Visibility = Visibility.Visible;
        itemsControl.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
    }

    private void SetSuggestionItems(
        Border panel,
        FrameworkElement loadingPanel,
        ItemsControl itemsControl,
        TextBlock targetLabel,
        string title,
        IEnumerable<string> suggestions)
    {
        var list = suggestions?.ToList() ?? new List<string>();
        panel.MinHeight = Math.Max(panel.MinHeight, CalculateSuggestionPanelMinHeight(list.Count));
        targetLabel.Text = title;
        loadingPanel.Visibility = Visibility.Collapsed;
        itemsControl.ItemsSource = list;
        itemsControl.Visibility = Visibility.Visible;
        panel.Visibility = Visibility.Visible;
    }

    private static double CalculateSuggestionPanelMinHeight(int expectedCount)
    {
        var count = Math.Max(1, expectedCount);
        const double headerHeight = 22;
        const double spacing = 8;
        const double itemHeight = 40;
        return headerHeight + spacing + (itemHeight * count);
    }
}
