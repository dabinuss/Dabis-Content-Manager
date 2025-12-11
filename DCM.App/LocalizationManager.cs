using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace DCM.App;

public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance =
        new(() => new LocalizationManager());

    public static LocalizationManager Instance => _instance.Value;

    private readonly ObservableCollection<LanguageInfo> _languages = new();
    public ReadOnlyObservableCollection<LanguageInfo> AvailableLanguages { get; }

    private ResourceDictionary? _baseDictionary;
    private ResourceDictionary? _currentLanguageDictionary;

    public string CurrentLanguage { get; private set; } = "en-US";

    private LocalizationManager()
    {
        AvailableLanguages = new ReadOnlyObservableCollection<LanguageInfo>(_languages);

        // Hier trägst du die Sprachen ein, die du unterstützt
        _languages.Add(new LanguageInfo("de-DE", "Language.Name.de"));
        _languages.Add(new LanguageInfo("en-US", "Language.Name.en"));
    }

    public void Initialize(string? languageFromSettings)
    {
        if (Application.Current is null)
        {
            throw new InvalidOperationException(
                "LocalizationManager.Initialize muss im WPF-Kontext aufgerufen werden.");
        }

        EnsureBaseDictionaryLoaded();

        if (!string.IsNullOrWhiteSpace(languageFromSettings))
        {
            SetLanguage(languageFromSettings);
        }
        else
        {
            SetLanguage(CurrentLanguage);
        }
    }

    public void SetLanguage(string languageCode)
    {
        if (Application.Current is null)
        {
            return;
        }

        EnsureBaseDictionaryLoaded();

        languageCode = NormalizeLanguageCode(languageCode);

        if (languageCode == CurrentLanguage && _currentLanguageDictionary is not null)
        {
            // Nichts zu tun
            return;
        }

        var merged = Application.Current.Resources.MergedDictionaries;

        // 1. Altes Sprachdictionary entfernen (falls vorhanden)
        if (_currentLanguageDictionary is not null && merged.Contains(_currentLanguageDictionary))
        {
            merged.Remove(_currentLanguageDictionary);
        }

        // 2. Neues Sprachdictionary laden
        var uriString = $"/DCM.App;component/Localization/Strings.{languageCode}.xaml";
        var uri = new Uri(uriString, UriKind.Relative);
        var newDict = new ResourceDictionary { Source = uri };

        // 3. Neues Sprachdictionary hinzufügen (nach dem Basis-Strings.xaml)
        merged.Add(newDict);

        _currentLanguageDictionary = newDict;
        CurrentLanguage = languageCode;

        OnPropertyChanged(nameof(CurrentLanguage));
        RefreshLanguageDisplayNames();
    }

    private void EnsureBaseDictionaryLoaded()
    {
        if (_baseDictionary is not null)
        {
            return;
        }

        // Basis-Strings (optional, z. B. Default / gemeinsame Keys)
        var uri = new Uri("/DCM.App;component/Localization/Strings.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        Application.Current.Resources.MergedDictionaries.Add(dict);
        _baseDictionary = dict;
    }

    private static string NormalizeLanguageCode(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "en-US";
        }

        languageCode = languageCode.Trim();

        // "de" → "de-DE"
        if (languageCode.StartsWith("de", StringComparison.OrdinalIgnoreCase))
        {
            return "de-DE";
        }

        // "en" → "en-US"
        if (languageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return "en-US";
        }

        return languageCode;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void RefreshLanguageDisplayNames()
    {
        foreach (var language in _languages)
        {
            language.RefreshDisplayName();
        }
    }
}

public sealed class LanguageInfo : INotifyPropertyChanged
{
    public string Code { get; }
    public string ResourceKey { get; }
    public string DisplayName => LocalizationHelper.Get(ResourceKey);

    public LanguageInfo(string code, string resourceKey)
    {
        Code = code;
        ResourceKey = resourceKey;
    }

    public override string ToString() => DisplayName;

    internal void RefreshDisplayName() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));

    public event PropertyChangedEventHandler? PropertyChanged;
}
