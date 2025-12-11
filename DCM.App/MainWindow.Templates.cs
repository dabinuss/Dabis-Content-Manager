using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using DCM.Core.Models;

namespace DCM.App;

public partial class MainWindow
{
    #region Templates

    private async Task LoadTemplatesAsync()
    {
        try
        {
            var templates = await Task.Run(() => _templateRepository.Load().ToList());

            _loadedTemplates.Clear();
            _loadedTemplates.AddRange(templates);

            var defaultTemplate = _loadedTemplates.FirstOrDefault(t => t.IsDefault && t.Platform == PlatformType.YouTube)
                                  ?? _loadedTemplates.FirstOrDefault(t => t.Platform == PlatformType.YouTube);

            TemplatesPageView?.BindTemplates(_loadedTemplates, defaultTemplate);
            LoadTemplateIntoEditor(defaultTemplate);

            UploadView.TemplateComboBox.ItemsSource = _loadedTemplates;
            if (defaultTemplate is not null)
            {
                UploadView.TemplateComboBox.SelectedItem = defaultTemplate;
            }
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Templates.LoadFailed", ex.Message);
        }
    }

    private void TemplateListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplatesPageView?.SelectedTemplate is Template tmpl)
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

        if (UploadView.TemplateComboBox.SelectedItem is not Template tmpl)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(UploadView.DescriptionTextBox.Text))
        {
            return;
        }

        var project = BuildUploadProjectFromUi(includeScheduling: false);
        var result = _templateService.ApplyTemplate(tmpl.Body, project);
        UploadView.DescriptionTextBox.Text = result;
        StatusTextBlock.Text = LocalizationHelper.Format("Status.Template.AutoApplied", tmpl.Name);
    }

    private void TemplateNewButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var tmpl = new Template
        {
            Name = LocalizationHelper.Get("Templates.DefaultName"),
            Platform = PlatformType.YouTube,
            IsDefault = !_loadedTemplates.Any(t => t.Platform == PlatformType.YouTube && t.IsDefault),
            Body = string.Empty
        };

        _loadedTemplates.Add(tmpl);
        RefreshTemplateBindings();

        TemplatesPageView?.SelectTemplate(tmpl);
        LoadTemplateIntoEditor(tmpl);

        StatusTextBlock.Text = LocalizationHelper.Get("Status.Template.Created");
    }

    private void TemplateEditButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (TemplatesPageView?.SelectedTemplate is Template tmpl)
        {
            LoadTemplateIntoEditor(tmpl);
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Template.Editing", tmpl.Name);
        }
        else
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Template.EditNone");
        }
    }

    private void TemplateDeleteButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (TemplatesPageView?.SelectedTemplate is not Template tmpl)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Template.DeleteNone");
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
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Template.Deleted", tmpl.Name);
        }
    }

    private void TemplateSaveButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            if (_currentEditingTemplate is null)
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Template.NoneToSave");
                return;
            }

            var editorState = TemplatesPageView?.TryGetEditorState();
            if (editorState is null)
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Template.EditorNotReady");
                return;
            }

            var name = editorState.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Template.NameRequired");
                return;
            }

            var platform = editorState.Platform;
            var isDefault = editorState.IsDefault;
            var description = editorState.Description;
            var body = editorState.Body;

            var duplicate = _loadedTemplates
                .FirstOrDefault(t =>
                    t.Platform == platform &&
                    !string.Equals(t.Id, _currentEditingTemplate.Id, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

            if (duplicate is not null)
            {
                StatusTextBlock.Text =
                    LocalizationHelper.Format("Status.Template.Duplicate", platform);
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

            TemplatesPageView?.BindTemplates(_loadedTemplates, _currentEditingTemplate);

            if (UploadView.TemplateComboBox is not null)
            {
                UploadView.TemplateComboBox.SelectedItem = _currentEditingTemplate;
            }

            StatusTextBlock.Text = LocalizationHelper.Format("Status.Template.Saved", _currentEditingTemplate.Name);
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Template.SaveFailed", ex.Message);
        }
    }

    private void LoadTemplateIntoEditor(Template? tmpl)
    {
        _currentEditingTemplate = tmpl;
        TemplatesPageView?.PopulateEditor(tmpl);
    }

    private void SaveTemplatesToRepository()
    {
        try
        {
            _templateRepository.Save(_loadedTemplates);
        }
        catch (System.Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Templates.SaveFailed", ex.Message);
        }
    }

    private void RefreshTemplateBindings()
    {
        TemplatesPageView?.BindTemplates(_loadedTemplates, _currentEditingTemplate);

        UploadView.TemplateComboBox.ItemsSource = null;
        UploadView.TemplateComboBox.ItemsSource = _loadedTemplates;
    }

    #endregion
}
