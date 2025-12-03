using System.Windows.Controls;
using DCM.Core.Models;

namespace DCM.App;

public partial class MainWindow
{
    #region Templates

    private void LoadTemplates()
    {
        try
        {
            _loadedTemplates.Clear();
            _loadedTemplates.AddRange(_templateRepository.Load());

            TemplateListBox.ItemsSource = _loadedTemplates;

            var defaultTemplate = _loadedTemplates.FirstOrDefault(t => t.IsDefault && t.Platform == PlatformType.YouTube)
                                  ?? _loadedTemplates.FirstOrDefault(t => t.Platform == PlatformType.YouTube);

            if (defaultTemplate is not null)
            {
                TemplateListBox.SelectedItem = defaultTemplate;
                LoadTemplateIntoEditor(defaultTemplate);
            }
            else
            {
                LoadTemplateIntoEditor(null);
            }

            TemplateComboBox.ItemsSource = _loadedTemplates;
            if (defaultTemplate is not null)
            {
                TemplateComboBox.SelectedItem = defaultTemplate;
            }
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = $"Templates konnten nicht geladen werden: {ex.Message}";
        }
    }

    private void TemplateListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateListBox.SelectedItem is Template tmpl)
        {
            LoadTemplateIntoEditor(tmpl);
        }
        else
        {
            LoadTemplateIntoEditor(null);
        }
    }

    private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settings.AutoApplyDefaultTemplate)
        {
            return;
        }

        if (TemplateComboBox.SelectedItem is not Template tmpl)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(DescriptionTextBox.Text))
        {
            return;
        }

        var project = BuildUploadProjectFromUi(includeScheduling: false);
        var result = _templateService.ApplyTemplate(tmpl.Body, project);
        DescriptionTextBox.Text = result;
        StatusTextBlock.Text = $"Template \"{tmpl.Name}\" automatisch angewendet.";
    }

    private void TemplateNewButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var tmpl = new Template
        {
            Name = "Neues Template",
            Platform = PlatformType.YouTube,
            IsDefault = !_loadedTemplates.Any(t => t.Platform == PlatformType.YouTube && t.IsDefault),
            Body = string.Empty
        };

        _loadedTemplates.Add(tmpl);
        RefreshTemplateBindings();

        TemplateListBox.SelectedItem = tmpl;
        LoadTemplateIntoEditor(tmpl);

        StatusTextBlock.Text = "Neues Template erstellt. Bitte bearbeiten und speichern.";
    }

    private void TemplateEditButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (TemplateListBox.SelectedItem is Template tmpl)
        {
            LoadTemplateIntoEditor(tmpl);
            StatusTextBlock.Text = $"Template \"{tmpl.Name}\" wird bearbeitet.";
        }
        else
        {
            StatusTextBlock.Text = "Kein Template zum Bearbeiten ausgewählt.";
        }
    }

    private void TemplateDeleteButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (TemplateListBox.SelectedItem is not Template tmpl)
        {
            StatusTextBlock.Text = "Kein Template zum Löschen ausgewählt.";
            return;
        }

        if (_loadedTemplates.Remove(tmpl))
        {
            if (_currentEditingTemplate?.Id == tmpl.Id)
            {
                _currentEditingTemplate = null;
                LoadTemplateIntoEditor(null);
            }

            SaveTemplatesToRepository();
            RefreshTemplateBindings();
            StatusTextBlock.Text = $"Template \"{tmpl.Name}\" gelöscht.";
        }
    }

    private void TemplateSaveButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            if (_currentEditingTemplate is null)
            {
                StatusTextBlock.Text = "Kein Template im Editor zum Speichern.";
                return;
            }

            var name = (TemplateNameTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                StatusTextBlock.Text = "Templatename darf nicht leer sein.";
                return;
            }

            var platform = PlatformType.YouTube;
            if (TemplatePlatformComboBox.SelectedItem is PlatformType selectedPlatform)
            {
                platform = selectedPlatform;
            }

            var isDefault = TemplateIsDefaultCheckBox.IsChecked == true;
            var description = TemplateDescriptionTextBox.Text;
            var body = TemplateBodyEditorTextBox.Text ?? string.Empty;

            var duplicate = _loadedTemplates
                .FirstOrDefault(t =>
                    t.Platform == platform &&
                    !string.Equals(t.Id, _currentEditingTemplate.Id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

            if (duplicate is not null)
            {
                StatusTextBlock.Text =
                    $"Hinweis: Es existiert bereits ein Template mit diesem Namen für Plattform {platform}.";
            }

            _currentEditingTemplate.Name = name;
            _currentEditingTemplate.Platform = platform;
            _currentEditingTemplate.IsDefault = isDefault;
            _currentEditingTemplate.Description = string.IsNullOrWhiteSpace(description) ? null : description;
            _currentEditingTemplate.Body = body;

            if (isDefault)
            {
                foreach (var other in _loadedTemplates.Where(t =>
                         t.Platform == platform && t.Id != _currentEditingTemplate.Id))
                {
                    other.IsDefault = false;
                }
            }

            SaveTemplatesToRepository();
            RefreshTemplateBindings();

            TemplateListBox.SelectedItem = _currentEditingTemplate;
            TemplateComboBox.SelectedItem = _currentEditingTemplate;

            StatusTextBlock.Text = $"Template \"{_currentEditingTemplate.Name}\" gespeichert.";
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = $"Fehler beim Speichern des Templates: {ex.Message}";
        }
    }

    private void LoadTemplateIntoEditor(Template? tmpl)
    {
        _currentEditingTemplate = tmpl;

        if (tmpl is null)
        {
            TemplateNameTextBox.Text = string.Empty;
            TemplatePlatformComboBox.SelectedItem = null;
            TemplateIsDefaultCheckBox.IsChecked = false;
            TemplateDescriptionTextBox.Text = string.Empty;
            TemplateBodyEditorTextBox.Text = string.Empty;
            return;
        }

        TemplateNameTextBox.Text = tmpl.Name;
        TemplatePlatformComboBox.SelectedItem = tmpl.Platform;
        TemplateIsDefaultCheckBox.IsChecked = tmpl.IsDefault;
        TemplateDescriptionTextBox.Text = tmpl.Description ?? string.Empty;
        TemplateBodyEditorTextBox.Text = tmpl.Body;
    }

    private void SaveTemplatesToRepository()
    {
        try
        {
            _templateRepository.Save(_loadedTemplates);
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = $"Templates konnten nicht gespeichert werden: {ex.Message}";
        }
    }

    private void RefreshTemplateBindings()
    {
        TemplateListBox.ItemsSource = null;
        TemplateListBox.ItemsSource = _loadedTemplates;

        TemplateComboBox.ItemsSource = null;
        TemplateComboBox.ItemsSource = _loadedTemplates;
    }

    #endregion
}