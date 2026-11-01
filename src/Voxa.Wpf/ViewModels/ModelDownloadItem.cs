using System.Windows.Input;
using Voxa.Core.Models;

namespace Voxa.Wpf.ViewModels;

/// <summary>One row of Settings → Models → Download models.</summary>
public sealed class ModelDownloadItem : ObservableObject
{
    private bool _isInstalled;
    private bool _isDownloading;
    private double _progress;
    private string _status = "";
    private string? _error;

    public ModelDownloadItem(ModelPackage package, Action<ModelDownloadItem> download, Action<ModelDownloadItem> cancel,
        Action<ModelDownloadItem> delete)
    {
        Package = package;
        DownloadCommand = new RelayCommand(() => download(this), () => CanDownload);
        CancelCommand = new RelayCommand(() => cancel(this), () => IsDownloading);
        DeleteCommand = new RelayCommand(() => delete(this), () => IsInstalled && !IsDownloading);
    }

    public ModelPackage Package { get; }

    public string Title => Package.Title;

    public string Description => Package.Description;

    public string SizeText => ModelDownloader.FormatSize(Package.TotalSize);

    public ICommand DownloadCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DeleteCommand { get; }

    /// <summary>Set by the owner while a download runs (it cancels it).</summary>
    internal CancellationTokenSource? Cancellation { get; set; }

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (!SetProperty(ref _isInstalled, value)) return;
            OnPropertyChanged(nameof(CanDownload));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (!SetProperty(ref _isDownloading, value)) return;
            OnPropertyChanged(nameof(CanDownload));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool CanDownload => !IsInstalled && !IsDownloading;

    /// <summary>0-100 for the progress bar.</summary>
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string? Error
    {
        get => _error;
        set
        {
            if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => Error is not null;
}
