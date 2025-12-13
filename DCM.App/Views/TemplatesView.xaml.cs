using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DCM.Core.Models;

namespace DCM.App.Views;

public partial class TemplatesView : UserControl
{
    public TemplatesView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? TemplateNewButtonClicked;
    public event RoutedEventHandler? TemplateEditButtonClicked;
    public event RoutedEventHandler? TemplateDeleteButtonClicked;
    public event RoutedEventHandler? TemplateSaveButtonClicked;
    public event SelectionChangedEventHandler? TemplateListBoxSelectionChanged;

    public Template? SelectedTemplate => TemplateListBox.SelectedItem as Template;

    public void BindTemplates(IEnumerable<Template> templates, Template? selectedTemplate)
    {
        TemplateListBox.ItemsSource = null;
        TemplateListBox.ItemsSource = templates;
        TemplateListBox.SelectedItem = selectedTemplate;
    }

    public void SelectTemplate(Template? template)
    {
        TemplateListBox.SelectedItem = template;
    }

    public void PopulateEditor(Template? template)
    {
        if (template is null)
        {
            TemplateNameTextBox.Text = string.Empty;
            TemplatePlatformComboBox.SelectedItem = null;
            TemplateIsDefaultCheckBox.IsChecked = false;
            TemplateDescriptionTextBox.Text = string.Empty;
            TemplateBodyEditorTextBox.Text = string.Empty;
            return;
        }

        TemplateNameTextBox.Text = template.Name;
        TemplatePlatformComboBox.SelectedItem = template.Platform;
        TemplateIsDefaultCheckBox.IsChecked = template.IsDefault;
        TemplateDescriptionTextBox.Text = template.Description ?? string.Empty;
        TemplateBodyEditorTextBox.Text = template.Body;
    }

    public void SetPlatformOptions(Array platforms)
    {
        TemplatePlatformComboBox.ItemsSource = platforms;
    }

    public void SetPlaceholders(IEnumerable<string> placeholders) =>
        PlaceholderItemsControl.ItemsSource = placeholders?.ToList() ?? new List<string>();

    public record TemplateEditorState(
        string Name,
        PlatformType Platform,
        bool IsDefault,
        string? Description,
        string Body);

    public TemplateEditorState? TryGetEditorState()
    {
        if (TemplateNameTextBox is null ||
            TemplatePlatformComboBox is null ||
            TemplateDescriptionTextBox is null ||
            TemplateBodyEditorTextBox is null ||
            TemplateIsDefaultCheckBox is null)
        {
            return null;
        }

        var name = (TemplateNameTextBox.Text ?? string.Empty).Trim();
        var platform = TemplatePlatformComboBox.SelectedItem is PlatformType selectedPlatform
            ? selectedPlatform
            : PlatformType.YouTube;
        var isDefault = TemplateIsDefaultCheckBox.IsChecked == true;
        var description = TemplateDescriptionTextBox.Text;
        var body = TemplateBodyEditorTextBox.Text ?? string.Empty;

        return new TemplateEditorState(name, platform, isDefault, description, body);
    }

    private void TemplateNewButton_Click(object sender, RoutedEventArgs e) =>
        TemplateNewButtonClicked?.Invoke(sender, e);

    private void TemplateEditButton_Click(object sender, RoutedEventArgs e) =>
        TemplateEditButtonClicked?.Invoke(sender, e);

    private void TemplateDeleteButton_Click(object sender, RoutedEventArgs e) =>
        TemplateDeleteButtonClicked?.Invoke(sender, e);

    private void TemplateSaveButton_Click(object sender, RoutedEventArgs e) =>
        TemplateSaveButtonClicked?.Invoke(sender, e);

    private void TemplateListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        TemplateListBoxSelectionChanged?.Invoke(sender, e);

    private void PlaceholderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Content is not string placeholder)
        {
            return;
        }

        if (TemplateBodyEditorTextBox is null)
        {
            return;
        }

        var tb = TemplateBodyEditorTextBox;
        tb.Focus();
        var caret = tb.CaretIndex;
        var text = tb.Text ?? string.Empty;
        tb.Text = text.Insert(caret, placeholder);
        tb.CaretIndex = caret + placeholder.Length;
    }
}
