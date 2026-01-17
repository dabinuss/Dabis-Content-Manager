using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace DCM.App;

internal static class WindowStateManager
{
    private sealed class WindowStateInfo
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsMaximized { get; set; }
    }

    private static string GetStoragePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "DabisContentManager");

        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "window-state.json");
    }

    public static void Apply(Window window)
    {
        try
        {
            var info = Load();
            if (info is null)
            {
                return;
            }

            if (info.Width <= 0 || info.Height <= 0)
            {
                return;
            }

            // Sicherstellen, dass das Fenster noch im sichtbaren Bereich liegt
            var savedRect = new Rect(info.Left, info.Top, info.Width, info.Height);
            var virtualRect = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            if (!virtualRect.IntersectsWith(savedRect))
            {
                // Fallback: Standard-Startposition aus XAML
                return;
            }

            window.WindowStartupLocation = WindowStartupLocation.Manual;

            window.Left = info.Left;
            window.Top = info.Top;
            window.Width = Math.Max(window.MinWidth, info.Width);
            window.Height = Math.Max(window.MinHeight, info.Height);

            if (info.IsMaximized)
            {
                window.WindowState = WindowState.Maximized;
            }
        }
        catch
        {
            // Falls etwas schiefgeht, einfach Standardwerte aus XAML verwenden
        }
    }

    public static void Save(Window window)
    {
        try
        {
            WindowStateInfo info;

            if (window.WindowState == WindowState.Normal)
            {
                info = new WindowStateInfo
                {
                    Left = window.Left,
                    Top = window.Top,
                    Width = window.Width,
                    Height = window.Height,
                    IsMaximized = false
                };
            }
            else
            {
                // Bei maximiertem Fenster die "RestoreBounds" speichern
                var bounds = window.RestoreBounds;
                info = new WindowStateInfo
                {
                    Left = bounds.Left,
                    Top = bounds.Top,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    IsMaximized = window.WindowState == WindowState.Maximized
                };
            }

            var path = GetStoragePath();
            var json = JsonSerializer.Serialize(
                info,
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(path, json);
        }
        catch
        {
            // Persistenzfehler sind nicht kritisch
        }
    }

    private static WindowStateInfo? Load()
    {
        try
        {
            var path = GetStoragePath();
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<WindowStateInfo>(json);
        }
        catch
        {
            return null;
        }
    }
}
