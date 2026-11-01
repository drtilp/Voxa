using System.IO;
using System.Windows.Input;
using Voxa.Wpf.Services;
using Voxa.Wpf.Setup;

namespace Voxa.Wpf.ViewModels;

/// <summary>
/// First-run model wizard (variant A, behind <see cref="FeatureFlags.FirstRunWizard"/>). Every step has the
/// recommended model preselected, so "Next, Next, Finish" gives a working setup.
/// Our models are downloaded after "Finish" (Settings → Models shows the progress).
/// </summary>
public sealed class SetupWizardViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogs;
    private int _index;
    private string _modelsFolder;

    public SetupWizardViewModel(IFileDialogService fileDialogs, string modelsFolder)
    {
        _fileDialogs = fileDialogs;
        _modelsFolder = modelsFolder;
        Steps =
        [
            new WizardStep(this, ModelRole.SpeechRecognition, "Speech recognition",
                "Turns the audio into text. Runs on this PC or in the cloud."),
            new WizardStep(this, ModelRole.Translation, "Translation",
                "Translates finished sentences into your language."),
            new WizardStep(this, ModelRole.VoiceActivity, "Voice activity detection",
                "Finds where sentences start and end. Keep the default unless you know why."),
        ];

        NextCommand = new RelayCommand(Next, () => IsSummary || CurrentStep!.IsComplete);
        BackCommand = new RelayCommand(() => Index--, () => Index > 0);
        SkipCommand = new RelayCommand(() => Skipped?.Invoke(this, EventArgs.Empty));
        ChangeFolderCommand = new RelayCommand(ChangeFolder);
    }

    /// <summary>The user finished the wizard; the app applies the choice to the settings.</summary>
    public event EventHandler<SetupChoice>? Finished;

    /// <summary>Closed without choosing; the wizard shows again on the next start.</summary>
    public event EventHandler? Skipped;

    public IReadOnlyList<WizardStep> Steps { get; }

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SkipCommand { get; }
    public ICommand ChangeFolderCommand { get; }

    private int Index
    {
        get => _index;
        set
        {
            if (!SetProperty(ref _index, Math.Clamp(value, 0, Steps.Count))) return;
            OnPropertyChanged(nameof(CurrentStep));
            OnPropertyChanged(nameof(IsSummary));
            OnPropertyChanged(nameof(StepCaption));
            OnPropertyChanged(nameof(NextText));
            OnPropertyChanged(nameof(Summary));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Null on the last page (summary).</summary>
    public WizardStep? CurrentStep => IsSummary ? null : Steps[Index];

    public bool IsSummary => Index == Steps.Count;

    public string StepCaption => $"Step {Index + 1} of {Steps.Count + 1}";

    public string NextText => IsSummary ? "Finish" : "Next";

    public string ModelsFolder
    {
        get => _modelsFolder;
        private set
        {
            if (SetProperty(ref _modelsFolder, value))
                OnPropertyChanged(nameof(Summary));
        }
    }

    public IReadOnlyList<SummaryLine> Summary => Steps.Select(step => new SummaryLine(
        step.Title, step.SelectedOffer.Title + (step.IsCustom ? $" ({step.CustomPath})" : ""), Note(step))).ToList();

    private string Note(WizardStep step)
    {
        var offer = step.SelectedOffer;
        if (!offer.IsAvailable) return "Coming soon: nothing will be set up for now.";
        return offer.Source switch
        {
            ModelSource.Download when File.Exists(PathOf(step)) || Directory.Exists(PathOf(step)) => "Already in the models folder.",
            ModelSource.Download => $"Downloads {offer.Size} after Finish (progress in Settings → Models).",
            ModelSource.Remote when offer.Id.StartsWith("cloud-", StringComparison.Ordinal) =>
                "Needs a sign-in, which is not available yet.",
            ModelSource.Remote => "Enter your API key in Settings → Translation.",
            _ => "",
        };
    }

    private string? PathOf(WizardStep step) => ModelOffers.ResolvePath(step.SelectedOffer, ModelsFolder, step.CustomPath);

    private void Next()
    {
        if (!IsSummary)
        {
            Index++;
            return;
        }

        var asr = Steps[0];
        var translation = Steps[1];
        var vad = Steps[2];
        Finished?.Invoke(this, new SetupChoice(
            ModelsFolder,
            PathOf(asr),
            PathOf(translation),
            PathOf(vad),
            translation.SelectedOffer.IsAvailable ? translation.SelectedOffer.Translator : null,
            ModelOffers.Downloads(asr.SelectedOffer, translation.SelectedOffer, vad.SelectedOffer)));
    }

    private void ChangeFolder()
    {
        if (_fileDialogs.PickFolder("Folder for the models", ModelsFolder) is { } folder)
            ModelsFolder = folder;
    }

    internal string? Browse(ModelRole role) => role switch
    {
        ModelRole.SpeechRecognition => _fileDialogs.PickFolder("Folder of your speech recognition model"),
        ModelRole.Translation => _fileDialogs.PickFile("Your translation model (.gguf)", FileFilters.Gguf),
        _ => _fileDialogs.PickFile("Your voice activity detection model (.onnx)", FileFilters.Onnx),
    };
}

/// <summary>One page of the wizard: the offers for one model role.</summary>
public sealed class WizardStep : ObservableObject
{
    private readonly SetupWizardViewModel _wizard;
    private ModelOffer _selectedOffer;
    private string? _customPath;

    public WizardStep(SetupWizardViewModel wizard, ModelRole role, string title, string subtitle)
    {
        _wizard = wizard;
        Role = role;
        Title = title;
        Subtitle = subtitle;
        Offers = ModelOffers.For(role);
        _selectedOffer = ModelOffers.Default(role);
        BrowseCommand = new RelayCommand(Browse);
    }

    public ModelRole Role { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public IReadOnlyList<ModelOffer> Offers { get; }
    public ICommand BrowseCommand { get; }

    public ModelOffer SelectedOffer
    {
        get => _selectedOffer;
        set
        {
            if (value is null || !SetProperty(ref _selectedOffer, value)) return;
            OnPropertyChanged(nameof(IsCustom));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsCustom => SelectedOffer.Source == ModelSource.Custom;

    public string? CustomPath
    {
        get => _customPath;
        private set
        {
            if (SetProperty(ref _customPath, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>A custom choice needs a path before "Next".</summary>
    public bool IsComplete => !IsCustom || !string.IsNullOrEmpty(CustomPath);

    private void Browse()
    {
        if (_wizard.Browse(Role) is { } path) CustomPath = path;
    }
}

public sealed record SummaryLine(string Role, string Choice, string Note);
