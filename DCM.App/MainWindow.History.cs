using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using DCM.Core.Models;

namespace DCM.App;

public partial class MainWindow
{
    #region History

    private async Task LoadUploadHistoryAsync()
    {
        try
        {
            var entries = await Task.Run(() => _uploadHistoryService
                .GetAll()
                .OrderByDescending(e => e.DateTime)
                .ToList());

            _allHistoryEntries = entries;
            ApplyHistoryFilter();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Upload-Historie konnte nicht geladen werden: {ex.Message}";
            _logger.Error($"Upload-Historie konnte nicht geladen werden: {ex.Message}", "History", ex);
        }
    }

    private void ApplyHistoryFilter()
    {
        if (HistoryPageView is null)
        {
            return;
        }

        IEnumerable<UploadHistoryEntry> filtered = _allHistoryEntries;

        var platformFilter = HistoryPageView.GetSelectedPlatformFilter();
        if (platformFilter is PlatformType platform)
        {
            filtered = filtered.Where(e => e.Platform == platform);
        }

        var statusTag = HistoryPageView.GetSelectedStatusFilter();
        if (statusTag == "Success")
        {
            filtered = filtered.Where(e => e.Success);
        }
        else if (statusTag == "Error")
        {
            filtered = filtered.Where(e => !e.Success);
        }

        HistoryPageView.SetHistoryItems(filtered.ToList());
    }

    private void OnHistoryFilterChanged()
    {
        ApplyHistoryFilter();
    }

    private void OnHistoryClearRequested()
    {
        var confirm = MessageBox.Show(
            this,
            "Die komplette Upload-Historie wirklich löschen?",
            "Bestätigung",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _uploadHistoryService.Clear();
            _allHistoryEntries.Clear();
            HistoryPageView?.SetHistoryItems(Array.Empty<UploadHistoryEntry>());
            StatusTextBlock.Text = "Upload-Historie gelöscht.";
            _logger.Info("Upload-Historie gelöscht", "History");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Historie konnte nicht gelöscht werden: {ex.Message}";
            _logger.Error($"Historie konnte nicht gelöscht werden: {ex.Message}", "History", ex);
        }
    }

    private void OnHistoryEntryOpenRequested(UploadHistoryEntry? entry)
    {
        if (entry?.VideoUrl is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(entry.VideoUrl.ToString())
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // Fehler ignorieren
            }
        }
    }

    private void OnHistoryLinkOpenRequested(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString())
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Fehler ignorieren
        }
    }

    #endregion
}
