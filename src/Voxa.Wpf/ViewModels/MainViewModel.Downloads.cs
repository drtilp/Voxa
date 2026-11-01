using System.Collections.ObjectModel;
using System.IO;
using Voxa.Core.Models;
using Voxa.Wpf.Setup;

namespace Voxa.Wpf.ViewModels;

/// <summary>Settings → Models → Download models: our models straight into the models folder.</summary>
public sealed partial class MainViewModel
{
    private readonly ModelDownloader _downloader = new(ModelDownloads.Http);

    public ObservableCollection<ModelDownloadItem> ModelDownloadItems { get; } = [];

    private void CreateDownloadItems()
    {
        foreach (var package in ModelDownloads.All)
            ModelDownloadItems.Add(new ModelDownloadItem(package, item => _ = DownloadAsync(item), CancelDownload, DeleteDownload));
        RefreshDownloadItems();
    }

    /// <summary>After a rescan of the models folder (files added or deleted by hand, folder changed).</summary>
    private void RefreshDownloadItems()
    {
        foreach (var item in ModelDownloadItems)
        {
            if (item.IsDownloading) continue;
            item.IsInstalled = ModelDownloader.IsInstalled(item.Package, ModelsFolder);
            long partial = item.IsInstalled ? 0 : ModelDownloader.DownloadedBytes(item.Package, ModelsFolder);
            item.Progress = item.IsInstalled ? 100 : 100.0 * partial / item.Package.TotalSize;
            item.Status = item.IsInstalled ? $"Downloaded · {item.SizeText}"
                : partial > 0 ? $"Paused at {ModelDownloader.FormatSize(partial)} of {item.SizeText}"
                : item.SizeText;
        }
    }

    /// <summary>Downloads the given packages one after another (setup wizard / installer choice).</summary>
    private async void DownloadPackages(IEnumerable<string> ids)
    {
        foreach (var item in ModelDownloadItems.Where(i => ids.Contains(i.Package.Id) && !i.IsInstalled && !i.IsDownloading).ToList())
            await DownloadAsync(item);
    }

    // Runs on the UI thread; Progress<T> brings the reports back here.
    private async Task DownloadAsync(ModelDownloadItem item)
    {
        if (item.IsDownloading) return;
        var folder = ModelsFolder;
        using var cts = new CancellationTokenSource();
        item.Cancellation = cts;
        item.IsDownloading = true;
        item.Error = null;
        item.Status = "Starting…";

        var progress = new Progress<DownloadProgress>(p =>
        {
            item.Progress = 100 * p.Fraction;
            item.Status = $"{ModelDownloader.FormatSize(p.Downloaded)} of {ModelDownloader.FormatSize(p.Total)}";
        });

        try
        {
            await Task.Run(() => _downloader.DownloadAsync(item.Package, folder, progress, cts.Token), cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Cancel: the .part files stay, Download continues from there.
        }
        catch (Exception ex) when (ex is ModelDownloadException or IOException or UnauthorizedAccessException)
        {
            item.Error = ex is UnauthorizedAccessException
                ? $"No permission to write into {folder}. Choose another models folder."
                : ex.Message;
        }
        finally
        {
            item.Cancellation = null;
            item.IsDownloading = false;
        }

        // The folder watcher picks the new model up as well; rescanning now makes it selectable right away.
        if (folder == ModelsFolder) ReloadModels();
        RefreshDownloadItems();
    }

    private static void CancelDownload(ModelDownloadItem item) => item.Cancellation?.Cancel();

    private void DeleteDownload(ModelDownloadItem item)
    {
        if (!_fileDialogs.Confirm($"Delete {item.Package.Title}?",
                $"Frees {item.SizeText} in {ModelsFolder}. You can download it again any time.", "Delete"))
            return;

        try
        {
            ModelDownloader.Delete(item.Package, ModelsFolder);
            item.Error = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // e.g. the model is loaded right now (Windows keeps the files open)
            item.Error = $"Can't delete: {ex.Message} Stop live transcription or switch to another model first.";
        }
        ReloadModels();
    }
}
