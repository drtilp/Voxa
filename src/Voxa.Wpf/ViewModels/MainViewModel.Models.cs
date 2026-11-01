using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Voxa.Core.Asr;
using Voxa.Wpf.Services;
using Voxa.Wpf.Setup;
using Voxa.Wpf.Transcription;

namespace Voxa.Wpf.ViewModels;

/// <summary>Models: the models folder, the selected speech recognition model and translator, VAD / language ID, setup.</summary>
public sealed partial class MainViewModel
{
    private AsrModelOption? _selectedModel;
    private ModelFile? _selectedTranslationModel; // the .gguf of the Local translator
    private ModelFile? _vadModel;
    private readonly ObservableCollection<ModelFile> _translationModels = [];
    private readonly ObservableCollection<ModelFile> _vadModels = [];
    private FileSystemWatcher? _modelsWatcher;
    private DispatcherTimer? _modelsChangedTimer;

    /// <summary>Raised by Settings → Experimental → "Open setup wizard"; the app owns the windows.</summary>
    public event EventHandler? SetupWizardRequested;

    public ICommand AddOnnxModelCommand { get; }
    public ICommand AddTranslationModelCommand { get; }
    public ICommand BrowseModelsFolderCommand { get; }
    public ICommand ResetModelsFolderCommand { get; }
    public ICommand OpenModelsFolderCommand { get; }
    public ICommand OpenSetupWizardCommand { get; }

    public ObservableCollection<AsrModelOption> Models { get; } = [];

    /// <summary>Settings → Translation: off, local .gguf models, the cloud server, the APIs (grouped in the view).</summary>
    public ObservableCollection<TranslationOption> TranslationOptions { get; } = [];

    public string ModelsFolder => _catalog.ModelsFolder;

    public bool IsDefaultModelsFolder => string.IsNullOrEmpty(_settings.ModelsFolder);

    public bool SetupCompleted => _settings.SetupCompleted;

    public AsrModelOption? SelectedModel
    {
        get => _selectedModel;
        set
        {
            // ListBox can push null while its items change; keep the last valid model.
            if (value is null || !SetProperty(ref _selectedModel, value)) return;
            UpdateSettings(s => s with { ModelPath = value.FilePath });
            RestartLiveIfRunning();
        }
    }

    /// <summary>One list for the translator and its local model, so a single click switches both (and loads once).</summary>
    public TranslationOption? SelectedTranslationOption
    {
        get => TranslationOptions.FirstOrDefault(o => o.Kind == SelectedTranslator
            && (o.Kind != TranslatorKind.Local || SamePath(o.ModelPath, _selectedTranslationModel?.Path)));
        set
        {
            // ListBox can push null while its items change.
            if (value is null || value == SelectedTranslationOption) return;
            if (value.ModelPath is { } path)
                _selectedTranslationModel = _translationModels.FirstOrDefault(m => SamePath(m.Path, path)) ?? ModelFile.FromPath(path);
            UpdateSettings(s => s with { Translator = value.Kind, TranslationModelPath = value.ModelPath ?? s.TranslationModelPath });
            OnTranslatorChanged();
            RecreateTranslator();
        }
    }

    /// <summary>Not selectable: Silero is the one that works (the cloud server runs its own VAD).</summary>
    public string VadModelText => _vadModel?.Name ?? "Missing — download Silero VAD under Models folder.";

    public bool HasVadModel => _vadModel is not null;

    // ---------- Feature flags ----------

    /// <summary>Variant A of the model choice: the wizard window on the first start (variant B is in the installer).</summary>
    public bool IsFirstRunWizardEnabled
    {
        get => _settings.Features.FirstRunWizard;
        set
        {
            if (value == _settings.Features.FirstRunWizard) return;
            UpdateSettings(s => s with { Features = s.Features with { FirstRunWizard = value } });
            OnPropertyChanged();
        }
    }

    // ---------- Setup (installer page or wizard) ----------

    /// <summary>Applies the model choice from the installer or the wizard and marks the setup as done.</summary>
    public void ApplySetup(SetupChoice choice)
    {
        string? folder = string.Equals(choice.ModelsFolder, ModelCatalog.DefaultModelsFolder, StringComparison.OrdinalIgnoreCase)
            ? null
            : choice.ModelsFolder;

        UpdateSettings(s => s with
        {
            ModelsFolder = folder,
            ModelPath = choice.AsrModelPath ?? s.ModelPath,
            CustomModelPaths = choice.AsrModelPath == ModelCatalog.CloudModelPath
                ? s.CustomModelPaths
                : AddPath(s.CustomModelPaths, choice.AsrModelPath),
            TranslationModelPath = choice.TranslationModelPath ?? s.TranslationModelPath,
            VadModelPath = choice.VadModelPath ?? s.VadModelPath,
            Translator = choice.Translator ?? s.Translator,
            SetupCompleted = true,
        });

        OnPropertyChanged(nameof(SetupCompleted));
        ReloadModels();
        OnTranslatorChanged();
        RecreateTranslator();
        RestartLiveIfRunning();
        // After ReloadModels: downloads go into the folder just chosen.
        if (choice.Downloads is { Count: > 0 } downloads) DownloadPackages(downloads);
    }

    public void SetModelsFolder(string? folder)
    {
        if (string.Equals(folder, ModelCatalog.DefaultModelsFolder, StringComparison.OrdinalIgnoreCase))
            folder = null;
        UpdateSettings(s => s with { ModelsFolder = folder });
        ReloadModels();
        if (SelectedTranslator == TranslatorKind.Local) RecreateTranslator();
        RestartLiveIfRunning();
    }

    /// <summary>Rescans the models folder and puts back the user's own models and the saved selection.</summary>
    private void ReloadModels()
    {
        _catalog = ModelCatalog.For(_settings.ModelsFolder);

        // Own models outside the folder survive a restart: the custom list plus whatever was selected last.
        var asr = _catalog.DiscoverAsrModels()
            .Concat(_settings.CustomModelPaths.Append(_settings.ModelPath).OfType<string>()
                .Select(ModelCatalog.TryCreateOption).OfType<AsrModelOption>())
            .DistinctBy(m => m.FilePath, StringComparer.OrdinalIgnoreCase)
            .Append(ModelCatalog.CloudModel);
        _selectedModel = Fill(Models, asr, m => m.FilePath, _settings.ModelPath);

        _selectedTranslationModel = Fill(_translationModels,
            WithSaved(_catalog.DiscoverTranslationModels(), _settings.TranslationModelPath),
            m => m.Path, _settings.TranslationModelPath);
        RefreshTranslationOptions();

        RefreshLanguageIdModel();

        _vadModel = Fill(_vadModels,
            WithSaved(_catalog.DiscoverVadModels(), _settings.VadModelPath),
            m => m.Path, _settings.VadModelPath);

        OnPropertyChanged(nameof(SelectedModel));
        OnPropertyChanged(nameof(VadModelText));
        OnPropertyChanged(nameof(HasVadModel));
        OnPropertyChanged(nameof(ModelsFolder));
        OnPropertyChanged(nameof(IsDefaultModelsFolder));
        CommandManager.InvalidateRequerySuggested();

        if (!string.Equals(_modelsWatcher?.Path, ModelsFolder, StringComparison.OrdinalIgnoreCase))
            WatchModelsFolder();
        RefreshDownloadItems();

        ErrorMessage = Models.All(m => m.Location == AsrModelLocation.Remote) && SelectedModel?.Location != AsrModelLocation.Remote
            ? $"No speech recognition model in {ModelsFolder}. Choose another folder, download a model in Settings, or pick the cloud model."
            : null;
    }

    // ---------- Watching the models folder ----------

    // Models copied in or deleted while Voxa runs show up without a restart.
    private void WatchModelsFolder()
    {
        StopWatchingModelsFolder();
        if (!_catalog.Exists) return;

        _modelsWatcher = new FileSystemWatcher(ModelsFolder)
        {
            IncludeSubdirectories = true,
            // LastWrite/Size: copying a 2 GB model raises Changed for a long time -> the debounce waits for its end.
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
        };
        _modelsWatcher.Created += OnModelsFolderChanged;
        _modelsWatcher.Deleted += OnModelsFolderChanged;
        _modelsWatcher.Changed += OnModelsFolderChanged;
        _modelsWatcher.Renamed += OnModelsFolderChanged;
        _modelsWatcher.Error += OnModelsFolderChanged; // buffer overflow: we missed events -> just rescan
        _modelsWatcher.EnableRaisingEvents = true;
    }

    private void StopWatchingModelsFolder()
    {
        if (_modelsWatcher is null) return;
        _modelsWatcher.EnableRaisingEvents = false;
        _modelsWatcher.Created -= OnModelsFolderChanged;
        _modelsWatcher.Deleted -= OnModelsFolderChanged;
        _modelsWatcher.Changed -= OnModelsFolderChanged;
        _modelsWatcher.Renamed -= OnModelsFolderChanged;
        _modelsWatcher.Error -= OnModelsFolderChanged;
        _modelsWatcher.Dispose();
        _modelsWatcher = null;
    }

    // Raised on a thread-pool thread, many times per copied file: restart a UI timer, rescan once it's quiet.
    private void OnModelsFolderChanged(object sender, EventArgs e) =>
        _dispatcher.BeginInvoke(() =>
        {
            if (_modelsChangedTimer is null)
            {
                _modelsChangedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _modelsChangedTimer.Tick += OnModelsFolderQuiet;
            }
            _modelsChangedTimer.Stop();
            _modelsChangedTimer.Start();
        });

    private void OnModelsFolderQuiet(object? sender, EventArgs e)
    {
        _modelsChangedTimer?.Stop();
        var translationModel = _selectedTranslationModel?.Path;
        ReloadModels();
        if (SelectedTranslator == TranslatorKind.Local && _selectedTranslationModel?.Path != translationModel)
            RecreateTranslator();
    }

    // Order = order of the groups in Settings → Translation.
    private void RefreshTranslationOptions()
    {
        TranslationOptions.Clear();
        TranslationOptions.Add(TranslationOption.Off);
        foreach (var model in _translationModels)
            TranslationOptions.Add(new TranslationOption(model.Name, TranslationOption.LocalGroup, TranslatorKind.Local, model.Path));
        TranslationOptions.Add(TranslationOption.Cloud);
        foreach (var api in TranslationOption.Apis) TranslationOptions.Add(api);
        OnPropertyChanged(nameof(SelectedTranslationOption));
    }

    private static bool SamePath(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <returns>The item matching <paramref name="savedPath"/>, else the first one.</returns>
    private static T? Fill<T>(ObservableCollection<T> target, IEnumerable<T> items, Func<T, string?> path, string? savedPath)
        where T : class
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
        return target.FirstOrDefault(i => string.Equals(path(i), savedPath, StringComparison.OrdinalIgnoreCase))
               ?? target.FirstOrDefault();
    }

    // A file picked from outside the models folder isn't found by the scan -> keep it in the list.
    private static IEnumerable<ModelFile> WithSaved(IReadOnlyList<ModelFile> found, string? savedPath) =>
        savedPath is not null && File.Exists(savedPath)
        && found.All(f => !string.Equals(f.Path, savedPath, StringComparison.OrdinalIgnoreCase))
            ? found.Append(ModelFile.FromPath(savedPath))
            : found;

    private static IReadOnlyList<string> AddPath(IReadOnlyList<string> paths, string? path) =>
        path is null || paths.Contains(path, StringComparer.OrdinalIgnoreCase) ? paths : [.. paths, path];

    private void AddOnnxModel()
    {
        var path = _fileDialogs.PickFile("Select a speech recognition model (.onnx)", FileFilters.Onnx);
        if (path is null) return;

        var option = ModelCatalog.TryCreateOption(path);
        if (option is null)
        {
            ErrorMessage = $"Not a sherpa-onnx model folder (encoder/decoder[/joiner] .onnx + tokens.txt): {Path.GetDirectoryName(path)}";
            return;
        }

        var existing = Models.FirstOrDefault(m => string.Equals(m.FilePath, option.FilePath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            Models.Add(option);
            UpdateSettings(s => s with { CustomModelPaths = AddPath(s.CustomModelPaths, option.FilePath) });
        }
        SelectedModel = existing ?? option;
        ErrorMessage = null;
    }

    private void AddTranslationModel()
    {
        if (_fileDialogs.PickFile("Select a translation model (.gguf)", FileFilters.Gguf) is not { } path) return;
        if (_translationModels.All(m => !SamePath(m.Path, path)))
        {
            _translationModels.Add(ModelFile.FromPath(path));
            RefreshTranslationOptions();
        }
        SelectedTranslationOption = TranslationOptions.First(o => SamePath(o.ModelPath, path));
    }

    private void BrowseModelsFolder()
    {
        if (_fileDialogs.PickFolder("Folder with models", ModelsFolder) is { } folder)
            SetModelsFolder(folder);
    }

    private void OpenModelsFolder()
    {
        if (Directory.Exists(ModelsFolder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ModelsFolder}\"") { UseShellExecute = true });
        else
            ErrorMessage = $"The models folder doesn't exist: {ModelsFolder}";
    }
}
