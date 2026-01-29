using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DCM.Core.Configuration;

/// <summary>
/// Basisklasse für Observable-Objekte mit INotifyPropertyChanged-Implementierung.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Setzt einen Property-Wert und löst PropertyChanged aus, wenn sich der Wert ändert.
    /// </summary>
    protected virtual bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Löst das PropertyChanged-Event aus.
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
