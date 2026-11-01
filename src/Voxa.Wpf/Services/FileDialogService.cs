using System.Windows;
using Voxa.Wpf.Views;
using Microsoft.Win32;

namespace Voxa.Wpf.Services;

/// <summary>Keeps dialogs out of the ViewModels (testable, no UI dependency there).</summary>
public interface IFileDialogService
{
    /// <param name="filter">WPF filter syntax, e.g. "ONNX model (*.onnx)|*.onnx".</param>
    string? PickFile(string title, string filter);

    string? PickFolder(string title, string? initialFolder = null);

    /// <returns>True when the user clicked <paramref name="confirmText"/>, false on Cancel / Esc / close.</returns>
    bool Confirm(string headline, string message, string confirmText);
}

public static class FileFilters
{
    public const string Onnx = "ONNX model (*.onnx)|*.onnx|All files (*.*)|*.*";
    public const string Gguf = "GGUF model (*.gguf)|*.gguf|All files (*.*)|*.*";
}

public sealed class FileDialogService : IFileDialogService
{
    public string? PickFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title, string? initialFolder = null)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (System.IO.Directory.Exists(initialFolder)) dialog.InitialDirectory = initialFolder;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public bool Confirm(string headline, string message, string confirmText)
    {
        var dialog = new ConfirmDialog(headline, message, confirmText);
        if (Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) is { } owner) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }
}
