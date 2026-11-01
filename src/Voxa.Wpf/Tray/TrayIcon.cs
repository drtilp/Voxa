using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Voxa.Wpf.Audio;
using Voxa.Wpf.Interop;
using Voxa.Wpf.Services;
using Voxa.Wpf.ViewModels;

namespace Voxa.Wpf.Tray;

/// <summary>
/// Tray icon (notification area next to the clock). WinForms NotifyIcon works inside WPF because
/// the WPF dispatcher pumps the same Win32 message loop.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _idleIcon = CreateIcon(recording: false);
    private readonly Icon _recordingIcon = CreateIcon(recording: true);
    private readonly Bitmap _hamburgerImage = CreateHamburgerImage();
    private readonly ToolStripMenuItem _modelMenu;
    private readonly ToolStripMenuItem _recordItem;
    private readonly ToolStripMenuItem _overlayItem;
    private readonly ToolStripMenuItem _captionTestItem;
    private readonly ToolStripMenuItem _liveItem;
    private readonly ToolStripMenuItem _inputMenu = new("Audio input");
    private readonly ToolStripMenuItem _translateMenu = new("Translate to");
    private readonly ToolStripMenuItem _updateItem;

    public TrayIcon(MainViewModel viewModel, Action showMain, Action showSettings, Action showUpdate, Action exit)
    {
        _viewModel = viewModel;

        // Always the same hamburger item; the selected model is marked by a check in its submenu.
        _modelMenu = new ToolStripMenuItem("Model", _hamburgerImage);
        _recordItem = new ToolStripMenuItem("Record", null, (_, _) => _viewModel.ToggleRecordingCommand.Execute(null));
        _overlayItem = new ToolStripMenuItem("Show overlay", null, (_, _) => _viewModel.IsOverlayVisible = !_viewModel.IsOverlayVisible);
        _captionTestItem = new ToolStripMenuItem("Test ASR Overlay UI", null, (_, _) => _viewModel.ToggleCaptionTestCommand.Execute(null));
        _recordItem.Visible = AppFeatures.Current.AudioTest;
        _captionTestItem.Visible = AppFeatures.Current.OverlayTest;
        _liveItem = new ToolStripMenuItem("Start live transcription", null, (_, _) => _viewModel.ToggleLiveCommand.Execute(null));
        _updateItem = new ToolStripMenuItem("Update", null, (_, _) => showUpdate()) { Visible = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Open Voxa", null, (_, _) => showMain()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => showSettings()));
        menu.Items.Add(_updateItem);
        menu.Items.Add(_modelMenu);
        menu.Items.Add(_inputMenu);
        menu.Items.Add(_translateMenu);
        menu.Items.Add(_liveItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_recordItem);
        menu.Items.Add(_captionTestItem);
        menu.Items.Add(_overlayItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => exit()));
        menu.Opening += (_, _) => UpdateMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = _idleIcon,
            Text = AppInfo.DisplayName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) showMain();
        };

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsRecording)) return;

        _notifyIcon.Icon = _viewModel.IsRecording ? _recordingIcon : _idleIcon;
        _notifyIcon.Text = _viewModel.IsRecording ? $"{AppInfo.DisplayName} – recording" : AppInfo.DisplayName;
    }

    private void UpdateMenu()
    {
        _recordItem.Text = _viewModel.IsRecording ? "Stop recording" : "Record";
        _overlayItem.Checked = _viewModel.IsOverlayVisible;
        _captionTestItem.Checked = _viewModel.IsCaptionTestRunning;
        _captionTestItem.Text = _viewModel.IsCaptionTestRunning ? "Stop ASR Overlay Test" : "Test ASR Overlay UI";
        _liveItem.Text = _viewModel.LiveButtonText;
        _liveItem.Checked = _viewModel.IsLive;
        _liveItem.Enabled = _viewModel.ToggleLiveCommand.CanExecute(null);
        _updateItem.Visible = _viewModel.HasUpdate;
        _updateItem.Text = _viewModel.HasUpdate ? $"Update to {_viewModel.AvailableUpdate!.Tag}" : "Update";
        _updateItem.Enabled = _viewModel.InstallUpdateCommand.CanExecute(null);
        RebuildModelMenu();
        RebuildInputMenu();
        RebuildTranslateMenu();
    }

    private void RebuildTranslateMenu()
    {
        foreach (ToolStripItem old in _translateMenu.DropDownItems.Cast<ToolStripItem>().ToList())
            old.Dispose();
        _translateMenu.DropDownItems.Clear();

        bool off = _viewModel.SelectedTranslator == TranslatorKind.Off;
        foreach (var language in _viewModel.TargetLanguages)
        {
            _translateMenu.DropDownItems.Add(new ToolStripMenuItem(language.Name, null, (_, _) => _viewModel.SelectedTargetLanguage = language)
            {
                Checked = !off && language == _viewModel.SelectedTargetLanguage,
                Enabled = !off,
            });
        }

        _translateMenu.DropDownItems.Add(new ToolStripSeparator());
        _translateMenu.DropDownItems.Add(new ToolStripMenuItem("Show original", null, (_, _) => _viewModel.ShowOriginal = !_viewModel.ShowOriginal)
        {
            Checked = _viewModel.ShowOriginal,
        });
        _translateMenu.Text = off ? "Translate to (off)" : $"Translate to: {_viewModel.SelectedTargetLanguage.Name}";
    }

    private void RebuildInputMenu()
    {
        foreach (ToolStripItem old in _inputMenu.DropDownItems.Cast<ToolStripItem>().ToList())
            old.Dispose();
        _inputMenu.DropDownItems.Clear();

        foreach (var input in _viewModel.AudioInputs)
        {
            var text = input == AudioInput.Microphone ? "Microphone (default)" : "System audio";
            _inputMenu.DropDownItems.Add(new ToolStripMenuItem(text, null, (_, _) => _viewModel.SelectedAudioInput = input)
            {
                Checked = input == _viewModel.SelectedAudioInput,
            });
        }
    }

    // Rebuilt on every open, so models added from disk (or in Settings) show up immediately.
    private void RebuildModelMenu()
    {
        foreach (ToolStripItem old in _modelMenu.DropDownItems.Cast<ToolStripItem>().ToList())
            old.Dispose();
        _modelMenu.DropDownItems.Clear();

        foreach (var model in _viewModel.Models)
        {
            _modelMenu.DropDownItems.Add(new ToolStripMenuItem(model.DisplayName, null, (_, _) => _viewModel.SelectedModel = model)
            {
                Checked = model == _viewModel.SelectedModel,
                ToolTipText = model.FilePath,
            });
        }

        _modelMenu.DropDownItems.Add(new ToolStripSeparator());
        _modelMenu.DropDownItems.Add(new ToolStripMenuItem("Add .onnx model…", null, (_, _) => _viewModel.AddOnnxModelCommand.Execute(null)));
    }

    private static Bitmap CreateHamburgerImage()
    {
        var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);
        using var pen = new Pen(Color.FromArgb(255, 63, 63, 70), 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var y in new[] { 4, 8, 12 })
            g.DrawLine(pen, 2, y, 14, y);
        return bitmap;
    }

    // The tile itself is Assets/Voxa.ico (generated by tools/IconGen); only the recording dot is drawn here.
    private static Icon CreateIcon(bool recording)
    {
        var size = SystemInformation.SmallIconSize; // 16 px at 100 % scaling, 24 px at 150 %...
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Voxa.ico"))
                       ?? throw new InvalidOperationException("Assets/Voxa.ico is missing from the resources.");
        using var stream = resource.Stream;
        var tile = new Icon(stream, size);
        if (!recording) return tile;

        using (tile)
        using (var bitmap = tile.ToBitmap())
        {
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float d = bitmap.Width * 0.42f;
                float x = bitmap.Width - d - 0.5f;
                using var dot = new SolidBrush(Color.FromArgb(255, 229, 72, 77));
                using var ring = new Pen(Color.FromArgb(255, 24, 24, 27), Math.Max(1f, bitmap.Width / 16f));
                g.FillEllipse(dot, x, x, d, d);
                g.DrawEllipse(ring, x, x, d, d);
            }

            // GetHicon creates an unmanaged handle that Icon.FromHandle doesn't own -> clone and destroy it.
            nint handle = bitmap.GetHicon();
            try
            {
                using var temp = Icon.FromHandle(handle);
                return (Icon)temp.Clone();
            }
            finally
            {
                NativeMethods.DestroyIcon(handle);
            }
        }
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _notifyIcon.Visible = false; // otherwise a "ghost" icon stays in the tray until hovered
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _idleIcon.Dispose();
        _recordingIcon.Dispose();
        _hamburgerImage.Dispose();
    }
}
