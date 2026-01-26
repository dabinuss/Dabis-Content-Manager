using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using DCM.App.Models; // For CategoryOption/LanguageOption interfaces if needed, or we use reflection/dynamic? 
// Actually simpler: Make it generic T where T : class
// And provide a selector/matcher function.

namespace DCM.App.Infrastructure;

/// <summary>
/// Manages a ComboBox's ItemsSource and Selection state to prevent race conditions
/// and ensuring fallback values are displayed correctly.
/// </summary>
/// <typeparam name="T">The type of the option (e.g. CategoryOption)</typeparam>
public sealed class SelectionManager<T> : IDisposable where T : class
{
    private readonly ComboBox _comboBox;
    private readonly Func<T, string> _idSelector;
    private readonly Func<string, T> _fallbackFactory;
    private bool _isUpdating;
    private bool _disposed;

    public event EventHandler<T?>? SelectionChanged;

    public SelectionManager(
        ComboBox comboBox,
        Func<T, string> idSelector,
        Func<string, T> fallbackFactory)
    {
        _comboBox = comboBox ?? throw new ArgumentNullException(nameof(comboBox));
        _idSelector = idSelector ?? throw new ArgumentNullException(nameof(idSelector));
        _fallbackFactory = fallbackFactory ?? throw new ArgumentNullException(nameof(fallbackFactory));

        _comboBox.SelectionChanged += OnComboBoxSelectionChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _comboBox.SelectionChanged -= OnComboBoxSelectionChanged;
        SelectionChanged = null;
        _disposed = true;
    }

    private void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // BLOCKER: If we are updating the list programmatically, ignore this event.
        if (_isUpdating)
        {
            return;
        }

        // Otherwise, it's a real user (or post-update) change.
        var selected = _comboBox.SelectedItem as T;
        SelectionChanged?.Invoke(this, selected);
    }

    /// <summary>
    /// Atomic update: Sets new options and restores selection without triggering unwanted events.
    /// </summary>
    /// <param name="newOptions">The new list of options from the source.</param>
    /// <param name="currentId">The ID that should be selected.</param>
    public void UpdateOptions(IEnumerable<T> newOptions, string? currentId)
    {
        _isUpdating = true;
        try
        {
            var list = newOptions.ToList();
            
            // 1. Handle Fallback if currentId is missing and not null/empty
            if (!string.IsNullOrWhiteSpace(currentId))
            {
                var exists = list.Any(item => string.Equals(_idSelector(item), currentId, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    // Created fallback item and add it to the list so it can be selected
                    list.Add(_fallbackFactory(currentId!));
                }
            }

            // 2. Update ItemsSource (Use ObservableCollection for immediate UI feedback)
            _comboBox.ItemsSource = new ObservableCollection<T>(list);

            // 3. Restore Selection
            if (string.IsNullOrWhiteSpace(currentId))
            {
                // Select "None" or null
                _comboBox.SelectedItem = list.FirstOrDefault(item => string.IsNullOrWhiteSpace(_idSelector(item)));
            }
            else
            {
                var match = list.FirstOrDefault(item => string.Equals(_idSelector(item), currentId, StringComparison.OrdinalIgnoreCase));
                _comboBox.SelectedItem = match;
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    /// <summary>
    /// Manually sets the selection by ID (safely).
    /// </summary>
    public void SelectById(string? id)
    {
        // We reuse the update logic style but without changing the list (unless fallback needed)
        
        // Simpler: Just try to select.
        if (string.IsNullOrWhiteSpace(id))
        {
             var emptyItem = _comboBox.Items.OfType<T>().FirstOrDefault(item => string.IsNullOrWhiteSpace(_idSelector(item)));
             if (emptyItem != null) _comboBox.SelectedItem = emptyItem;
             else _comboBox.SelectedItem = null;
             return;
        }

        var match = _comboBox.Items.OfType<T>().FirstOrDefault(item => string.Equals(_idSelector(item), id, StringComparison.OrdinalIgnoreCase));
        
        if (match != null)
        {
            _comboBox.SelectedItem = match;
            return;
        }

        // Fallback needed?
        // If we are strictly selecting, we might need to add fallback.
        // But adding to ItemsSource requires ItemsSource to be a collection we can modify.
        // We assume it is ObservableCollection<T> from UpdateOptions.
        
        if (_comboBox.ItemsSource is ObservableCollection<T> collection)
        {
            var fallback = _fallbackFactory(id);
            collection.Add(fallback);
            _comboBox.SelectedItem = fallback;
        }
    }
}
