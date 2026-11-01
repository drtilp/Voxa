; Voxa installer (Inno Setup 6.7+). Build: installer\build.ps1 (publishes the app, then runs ISCC).
; Manually: ISCC.exe installer\Voxa.iss [/DModelSelectionPage=0] [/DPublishDir=...]

; ---------- Feature flags ----------
; Variant B of the model choice: pages in the installer. 0 = no pages, no install.json; the app then uses
; its own first-run wizard (variant A) when that flag is on in the app settings.
#ifndef ModelSelectionPage
  #define ModelSelectionPage 1
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\installer"
#endif

#define AppName "Voxa"
#define AppExe "Voxa.exe"
; Version comes from the published exe (<Version> in Voxa.Wpf.csproj), so there is one place to bump it.
#define AppVersion GetVersionNumbersString(PublishDir + "\" + AppExe)

[Setup]
; Never change AppId: Windows uses it to recognize upgrades and the uninstaller.
AppId={{51B0CFBB-A1B7-416B-83DC-636525B0CD9D}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Pavel Drtil
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; Program Files needs admin; Windows shows the standard UAC prompt.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
; Stable name: README links .../releases/latest/download/Voxa-Setup.exe and the app's updater looks for it.
OutputBaseFilename={#AppName}-Setup
SetupIconFile=..\src\Voxa.Wpf\Assets\Voxa.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
; Follows the Windows light/dark setting, like the app's dark UI.
WizardStyle=modern dynamic
; A running Voxa is detected by its single-instance mutex in InitializeSetup (not AppMutex: an update started
; from the app must wait a moment until the app exits, AppMutex would fail right away).
CloseApplications=yes
; Shortcuts go to "All users" areas when elevated; the startup shortcut is per machine on purpose.
UsedUserAreasWarning=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "czech"; MessagesFile: "compiler:Languages\Czech.isl"

[CustomMessages]
; Every text Setup shows itself, in both languages of [Languages]. Texts of Windows' own pages come from the .isl files.
english.AutostartTask=Start Voxa when Windows starts
czech.AutostartTask=Spouštět Voxa při startu Windows
english.VoxaRunning=Voxa is running. Close it (tray icon → Exit) and click Retry.
czech.VoxaRunning=Voxa právě běží. Ukončete ji (ikona v oznamovací oblasti → Exit) a klikněte na Opakovat.

english.RuntimeDownloading=Downloading the .NET %1 Desktop Runtime from Microsoft (about 60 MB)…
czech.RuntimeDownloading=Stahuje se .NET %1 Desktop Runtime od Microsoftu (asi 60 MB)…
english.RuntimeDownloadFailed=The .NET Desktop Runtime could not be downloaded: %1%n%nCheck the internet connection, or install it from https://dotnet.microsoft.com/download/dotnet/%2.0 and run Setup again.
czech.RuntimeDownloadFailed=Nepodařilo se stáhnout .NET Desktop Runtime: %1%n%nZkontrolujte připojení k internetu, nebo ho nainstalujte z https://dotnet.microsoft.com/download/dotnet/%2.0 a spusťte instalaci znovu.
english.RuntimeInstalling=Installing the .NET %1 Desktop Runtime…
czech.RuntimeInstalling=Instaluje se .NET %1 Desktop Runtime…
english.RuntimeCannotStart=The .NET Desktop Runtime installer could not be started.
czech.RuntimeCannotStart=Instalátor .NET Desktop Runtime se nepodařilo spustit.
english.RuntimeFailed=The .NET Desktop Runtime installation failed (exit code %1).
czech.RuntimeFailed=Instalace .NET Desktop Runtime selhala (kód chyby %1).

; ---------- Model pages ----------
english.AsrTitle=Speech recognition
czech.AsrTitle=Rozpoznávání řeči
english.AsrDescription=Which models should turn the audio into text?
czech.AsrDescription=Které modely mají převádět zvuk na text?
english.AsrHelp=Tick one or more models.%nLocal = the model is downloaded from Hugging Face into files on this PC after the installation, when Voxa starts for the first time (progress in Voxa Settings → Models). Remote model = it runs on a server, nothing is downloaded.%nVoxa starts with the first ticked model (an own model goes first); you can switch to the others in Settings → Models.
czech.AsrHelp=Zaškrtněte jeden nebo více modelů.%nLokální = model se po dokončení instalace, při prvním spuštění Voxa, stáhne z Hugging Face do souborů v tomto počítači (průběh v nastavení Voxa → Models). Vzdálený model = běží na serveru, nic se nestahuje.%nVoxa začne s prvním zaškrtnutým modelem (vlastní model má přednost), na ostatní se přepnete v nastavení → Models.
english.AsrParakeet=Parakeet TDT 0.6B v3  (local, ≈ 670 MB, recommended)
czech.AsrParakeet=Parakeet TDT 0.6B v3  (lokální, ≈ 670 MB, doporučeno)
english.AsrWhisper=Whisper large-v3 turbo  (local, ≈ 1.0 GB, 99 languages, slower)
czech.AsrWhisper=Whisper large-v3 turbo  (lokální, ≈ 1,0 GB, 99 jazyků, pomalejší)
english.AsrRemote=Parakeet TDT 0.6B v3  (Remote model, no download)
czech.AsrRemote=Parakeet TDT 0.6B v3  (vzdálený model, bez stahování)
english.AsrOwn=Own model  (a sherpa-onnx folder on this PC)
czech.AsrOwn=Vlastní model  (složka sherpa-onnx v tomto počítači)
english.Later=Select and download later
czech.Later=Vybrat a stáhnout později

english.AsrOwnTitle=Own speech recognition model
czech.AsrOwnTitle=Vlastní model pro rozpoznávání řeči
english.WhereIsModel=Where is the model?
czech.WhereIsModel=Kde je model?
english.AsrOwnHelp=Select the folder with the encoder / decoder .onnx files and tokens.txt.
czech.AsrOwnHelp=Vyberte složku se soubory encoder / decoder (.onnx) a tokens.txt.

english.TrTitle=Translation
czech.TrTitle=Překlad
english.TrDescription=How should Voxa translate?
czech.TrDescription=Čím má Voxa překládat?
english.TrHelp=Finished sentences are translated into your language. Tick one or more; with nothing ticked Voxa only shows the transcript. Local models are downloaded after the installation, remote models and API keys need no download.
czech.TrHelp=Dokončené věty se přeloží do vašeho jazyka. Zaškrtněte jeden nebo více; bez zaškrtnutí Voxa zobrazí jen přepis. Lokální modely se stáhnou po instalaci, vzdálené modely a API klíče nic nestahují.
english.TrHy=Hy-MT2 1.8B  (local on the GPU, ≈ 1.9 GB, recommended)
czech.TrHy=Hy-MT2 1.8B  (lokální na GPU, ≈ 1,9 GB, doporučeno)
english.TrHyRemote=Hy-MT2 1.8B  (Remote model, no download)
czech.TrHyRemote=Hy-MT2 1.8B  (vzdálený model, bez stahování)
english.TrDeepL=DeepL API  (your own key, entered in Voxa Settings)
czech.TrDeepL=DeepL API  (váš vlastní klíč, zadá se v nastavení Voxa)
english.TrAzure=Azure Translator  (your own key, entered in Voxa Settings)
czech.TrAzure=Azure Translator  (váš vlastní klíč, zadá se v nastavení Voxa)
english.TrOwn=Own model  (a .gguf file on this PC)
czech.TrOwn=Vlastní model  (soubor .gguf v tomto počítači)
english.TrOwnTitle=Own translation model
czech.TrOwnTitle=Vlastní překladový model
english.TrOwnHelp=Select a .gguf translation model.
czech.TrOwnHelp=Vyberte překladový model ve formátu .gguf.
english.ModelFile=Model file:
czech.ModelFile=Soubor modelu:
english.GgufFilter=GGUF model (*.gguf)|*.gguf|All files|*.*
czech.GgufFilter=Model GGUF (*.gguf)|*.gguf|Všechny soubory|*.*

english.VadTitle=Voice activity detection
czech.VadTitle=Detekce hlasové aktivity
english.VadDescription=Which model should find where sentences start and end?
czech.VadDescription=Který model má určit, kde věty začínají a končí?
english.VadHelp=Needed for local speech recognition. Keep the default unless you know why.
czech.VadHelp=Potřebné pro lokální rozpoznávání řeči. Ponechte výchozí volbu, pokud nemáte důvod ji měnit.
english.VadSilero=Silero VAD  (≈ 2 MB, recommended)
czech.VadSilero=Silero VAD  (≈ 2 MB, doporučeno)
english.VadOwn=Own model  (an .onnx file on this PC)
czech.VadOwn=Vlastní model  (soubor .onnx v tomto počítači)
english.VadOwnTitle=Own voice activity detection model
czech.VadOwnTitle=Vlastní model detekce hlasové aktivity
english.VadOwnHelp=Select a VAD .onnx model.
czech.VadOwnHelp=Vyberte model VAD ve formátu .onnx.
english.OnnxFilter=ONNX model (*.onnx)|*.onnx|All files|*.*
czech.OnnxFilter=Model ONNX (*.onnx)|*.onnx|Všechny soubory|*.*

english.SelectFolder=Select an existing folder.
czech.SelectFolder=Vyberte existující složku.
english.SelectGguf=Select an existing .gguf file.
czech.SelectGguf=Vyberte existující soubor .gguf.
english.SelectOnnx=Select an existing .onnx file.
czech.SelectOnnx=Vyberte existující soubor .onnx.
english.SelectAtLeastOne=Tick at least one model, or "Select and download later".
czech.SelectAtLeastOne=Zaškrtněte alespoň jeden model, nebo „Vybrat a stáhnout později“.

; ---------- "Ready to install" summary and uninstall ----------
english.MemoModelsUnchanged=Models: unchanged (update)
czech.MemoModelsUnchanged=Modely: beze změny (aktualizace)
english.MemoRuntime=.NET %1 Desktop Runtime: will be downloaded and installed
czech.MemoRuntime=.NET %1 Desktop Runtime: bude stažen a nainstalován
english.MemoModels=Models:
czech.MemoModels=Modely:
english.MemoAsr=Speech recognition:
czech.MemoAsr=Rozpoznávání řeči:
english.MemoTr=Translation:
czech.MemoTr=Překlad:
english.MemoVad=Voice activity detection:
czech.MemoVad=Detekce hlasové aktivity:
english.MemoNone=none
czech.MemoNone=žádný
english.MemoLater=Selected and downloaded later, in Voxa Settings → Models
czech.MemoLater=Výběr a stažení později, v nastavení Voxa → Models
english.MemoDownload=Local models are downloaded from Hugging Face after the installation (progress in Voxa Settings → Models).
czech.MemoDownload=Lokální modely se po instalaci stáhnou z Hugging Face (průběh v nastavení Voxa → Models).
english.UninstallModels=Also delete the models in %1?
czech.UninstallModels=Smazat také modely ve složce %1?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
; Users may write here without admin: models are downloaded / added later from the app.
Name: "{app}\models"; Permissions: users-modify

[InstallDelete]
; Files of the previous version that the new one no longer has must go: 0.1 was self-contained, and its
; app-local runtime (hostfxr.dll, coreclr.dll...) left next to Voxa.exe would be used instead of the
; installed .NET. Inno Setup only adds/overwrites files on upgrade, so clean the app files first.
; models\ and install.json stay. Only in a Voxa folder: a fresh install into a folder of other programs
; must not delete their DLLs.
Type: files; Name: "{app}\*.dll"; Check: IsVoxaFolder
Type: files; Name: "{app}\*.pdb"; Check: IsVoxaFolder
Type: files; Name: "{app}\createdump.exe"; Check: IsVoxaFolder
Type: files; Name: "{app}\Voxa.deps.json"; Check: IsVoxaFolder
Type: files; Name: "{app}\Voxa.runtimeconfig.json"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\runtimes"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\cs"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\de"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\es"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\fr"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\it"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\ja"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\ko"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\pl"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\pt-BR"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\ru"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\tr"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\zh-Hans"; Check: IsVoxaFolder
Type: filesandordirs; Name: "{app}\zh-Hant"; Check: IsVoxaFolder

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start menu entry = the app shows up in Windows Search.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{autostartup}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: autostart

[Run]
; runasoriginaluser: Setup runs elevated, Voxa must not (its settings belong to the signed-in user).
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Update started from Voxa (/SILENT /fromapp=1): start it again. Voxa started Setup elevated ("runas"), so
; runasoriginaluser would mean the admin token; explorer.exe hands the start to the user's own (non-elevated) shell.
Filename: "{win}\explorer.exe"; Parameters: {app}\{#AppExe}; Flags: nowait; Check: StartedByUpdater

[UninstallDelete]
Type: files; Name: "{app}\install.json"

[Code]
const
  AppMutexName = 'Voxa.SingleInstance';

// [InstallDelete] guard: clean up only where Voxa is installed already (upgrade).
function IsVoxaFolder: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\{#AppExe}'));
end;

function StartedByUpdater: Boolean;
begin
  Result := ExpandConstant('{param:fromapp|0}') = '1';
end;

function WaitForVoxaToExit(TimeoutMs: Integer): Boolean;
var
  Waited: Integer;
begin
  Waited := 0;
  while CheckForMutexes(AppMutexName) and (Waited < TimeoutMs) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
  Result := not CheckForMutexes(AppMutexName);
end;

// Voxa locks its files while running. The updater exits right after starting Setup, so give it time.
function EnsureVoxaClosed: Boolean;
var
  Timeout: Integer;
begin
  if StartedByUpdater then Timeout := 15000 else Timeout := 500;
  Result := WaitForVoxaToExit(Timeout);
  while not Result do
  begin
    if SuppressibleMsgBox(CustomMessage('VoxaRunning'), mbError, MB_RETRYCANCEL, IDCANCEL) = IDCANCEL then
      Exit;
    Result := WaitForVoxaToExit(2000);
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := EnsureVoxaClosed;
end;

function InitializeUninstall: Boolean;
begin
  Result := EnsureVoxaClosed;
end;

// ---------- .NET Desktop Runtime ----------
// Voxa is framework-dependent (no runtime copy in Program Files). Without the runtime Voxa.exe would only
// show "You must install .NET" with a link, so Setup checks for it and installs it from Microsoft when missing.
// Separate event handlers (<event(...)>) keep this independent of the model pages below.
const
  DotNetMajor = '10';
  DotNetInstallerUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  DotNetInstallerFile = 'windowsdesktop-runtime-win-x64.exe';

var
  RuntimeDownloadPage: TDownloadWizardPage;
  RuntimeDownloaded: Boolean;

function IsDesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  // Any 10.x: the app rolls forward to the newest installed patch / minor version.
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\' + DotNetMajor + '.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

<event('InitializeWizard')>
procedure InitializeRuntimeDownload;
begin
  RuntimeDownloadPage := CreateDownloadPage('.NET Desktop Runtime',
    FmtMessage(CustomMessage('RuntimeDownloading'), [DotNetMajor]), nil);
end;

<event('NextButtonClick')>
function DownloadRuntimeIfMissing(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID <> wpReady) or RuntimeDownloaded or IsDesktopRuntimeInstalled then
    Exit;

  RuntimeDownloadPage.Clear;
  RuntimeDownloadPage.Add(DotNetInstallerUrl, DotNetInstallerFile, '');
  RuntimeDownloadPage.Show;
  try
    try
      RuntimeDownloadPage.Download;
      RuntimeDownloaded := True;
    except
      if not RuntimeDownloadPage.AbortedByUser then
        SuppressibleMsgBox(FmtMessage(CustomMessage('RuntimeDownloadFailed'), [GetExceptionMessage, DotNetMajor]),
          mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  finally
    RuntimeDownloadPage.Hide;
  end;
end;

// Runs before the files are copied; a non-empty result stops Setup with that message.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if not RuntimeDownloaded then
    Exit;
  WizardForm.PreparingLabel.Caption := FmtMessage(CustomMessage('RuntimeInstalling'), [DotNetMajor]);
  if not Exec(ExpandConstant('{tmp}\' + DotNetInstallerFile), '/install /quiet /norestart', '',
    SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    Result := CustomMessage('RuntimeCannotStart')
  else if ResultCode = 3010 then
    NeedsRestart := True // installed, Windows wants a restart
  else if ResultCode <> 0 then
    Result := FmtMessage(CustomMessage('RuntimeFailed'), [IntToStr(ResultCode)]);
end;

#if ModelSelectionPage
var
  AsrPage, TranslationPage, VadPage: TInputOptionWizardPage;
  AsrCustomPage: TInputDirWizardPage;
  TranslationCustomPage, VadCustomPage: TInputFileWizardPage;
  WriteManifestAfterInstall: Boolean;

{ Ids must match ModelOffers.cs in the app. Index = order of the items below. The item after the models of the
  speech recognition page is "Select and download later". }
const
  AsrCount = 4;
  AsrCustomIndex = 3;
  AsrLaterIndex = 4;
  TranslationCount = 5;
  TranslationCustomIndex = 4;
  VadCustomIndex = 1;

function AsrId(Index: Integer): String;
begin
  case Index of
    0: Result := 'parakeet-tdt-0.6b-v3';
    1: Result := 'whisper-large-v3-turbo';
    2: Result := 'cloud-asr';
  else
    Result := 'custom-asr';
  end;
end;

function TranslationId(Index: Integer): String;
begin
  case Index of
    0: Result := 'hy-mt2-1.8b';
    1: Result := 'cloud-translation';
    2: Result := 'deepl';
    3: Result := 'azure';
  else
    Result := 'custom-translation';
  end;
end;

function VadId(Index: Integer): String;
begin
  if Index = 0 then Result := 'silero-vad' else Result := 'custom-vad';
end;

// "Select and download later" excludes the models and the other way round.
procedure AsrClickCheck(Sender: TObject);
var
  Clicked, I: Integer;
begin
  Clicked := AsrPage.CheckListBox.ItemIndex;
  if Clicked < 0 then
    Exit;
  if Clicked = AsrLaterIndex then
  begin
    if AsrPage.Values[AsrLaterIndex] then
      for I := 0 to AsrCount - 1 do
        AsrPage.Values[I] := False;
  end
  else if AsrPage.Values[Clicked] then
    AsrPage.Values[AsrLaterIndex] := False;
end;

procedure InitializeWizard;
begin
  AsrPage := CreateInputOptionPage(wpSelectTasks,
    CustomMessage('AsrTitle'), CustomMessage('AsrDescription'), CustomMessage('AsrHelp'), False, False);
  AsrPage.Add(CustomMessage('AsrParakeet'));
  AsrPage.Add(CustomMessage('AsrWhisper'));
  AsrPage.Add(CustomMessage('AsrRemote'));
  AsrPage.Add(CustomMessage('AsrOwn'));
  AsrPage.Add(CustomMessage('Later'));
  AsrPage.Values[0] := True;
  AsrPage.CheckListBox.OnClickCheck := @AsrClickCheck;

  AsrCustomPage := CreateInputDirPage(AsrPage.ID,
    CustomMessage('AsrOwnTitle'), CustomMessage('WhereIsModel'), CustomMessage('AsrOwnHelp'), False, '');
  AsrCustomPage.Add('');

  TranslationPage := CreateInputOptionPage(AsrCustomPage.ID,
    CustomMessage('TrTitle'), CustomMessage('TrDescription'), CustomMessage('TrHelp'), False, False);
  TranslationPage.Add(CustomMessage('TrHy'));
  TranslationPage.Add(CustomMessage('TrHyRemote'));
  TranslationPage.Add(CustomMessage('TrDeepL'));
  TranslationPage.Add(CustomMessage('TrAzure'));
  TranslationPage.Add(CustomMessage('TrOwn'));
  TranslationPage.Values[0] := True;

  TranslationCustomPage := CreateInputFilePage(TranslationPage.ID,
    CustomMessage('TrOwnTitle'), CustomMessage('WhereIsModel'), CustomMessage('TrOwnHelp'));
  TranslationCustomPage.Add(CustomMessage('ModelFile'), CustomMessage('GgufFilter'), '.gguf');

  VadPage := CreateInputOptionPage(TranslationCustomPage.ID,
    CustomMessage('VadTitle'), CustomMessage('VadDescription'), CustomMessage('VadHelp'), True, False);
  VadPage.Add(CustomMessage('VadSilero'));
  VadPage.Add(CustomMessage('VadOwn'));
  VadPage.SelectedValueIndex := 0;

  VadCustomPage := CreateInputFilePage(VadPage.ID,
    CustomMessage('VadOwnTitle'), CustomMessage('WhereIsModel'), CustomMessage('VadOwnHelp'));
  VadCustomPage.Add(CustomMessage('ModelFile'), CustomMessage('OnnxFilter'), '.onnx');
end;

// Upgrade: the models were chosen at the first install (install.json is kept) -> don't ask again.
function ModelsChosenBefore: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\install.json'));
end;

function IsModelPage(PageID: Integer): Boolean;
begin
  Result := (PageID = AsrPage.ID) or (PageID = AsrCustomPage.ID) or (PageID = TranslationPage.ID) or
    (PageID = TranslationCustomPage.ID) or (PageID = VadPage.ID) or (PageID = VadCustomPage.ID);
end;

function ChoseLater: Boolean;
begin
  Result := AsrPage.Values[AsrLaterIndex];
end;

// The voice activity model is needed by every local speech recognition model; a remote one has its own.
function LocalAsrTicked: Boolean;
begin
  Result := not ChoseLater and (AsrPage.Values[0] or AsrPage.Values[1] or AsrPage.Values[AsrCustomIndex]);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if not IsModelPage(PageID) then
    Exit;
  // Nested ifs, not "and": {app} is unknown before the directory page, ModelsChosenBefore must not run there.
  if ModelsChosenBefore then
  begin
    Result := True;
    Exit;
  end;
  if PageID = AsrPage.ID then
    Exit;
  if ChoseLater then
  begin
    Result := True;
    Exit;
  end;
  Result :=
    ((PageID = AsrCustomPage.ID) and not AsrPage.Values[AsrCustomIndex]) or
    ((PageID = TranslationCustomPage.ID) and not TranslationPage.Values[TranslationCustomIndex]) or
    ((PageID = VadPage.ID) and not LocalAsrTicked) or
    ((PageID = VadCustomPage.ID) and (not LocalAsrTicked or (VadPage.SelectedValueIndex <> VadCustomIndex)));
end;

function AnyAsrOptionTicked: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to AsrLaterIndex do
    if AsrPage.Values[I] then
      Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if (CurPageID = AsrPage.ID) and not AnyAsrOptionTicked then
  begin
    MsgBox(CustomMessage('SelectAtLeastOne'), mbError, MB_OK);
    Result := False;
  end
  else if (CurPageID = AsrCustomPage.ID) and not DirExists(AsrCustomPage.Values[0]) then
  begin
    MsgBox(CustomMessage('SelectFolder'), mbError, MB_OK);
    Result := False;
  end
  else if (CurPageID = TranslationCustomPage.ID) and not FileExists(TranslationCustomPage.Values[0]) then
  begin
    MsgBox(CustomMessage('SelectGguf'), mbError, MB_OK);
    Result := False;
  end
  else if (CurPageID = VadCustomPage.ID) and not FileExists(VadCustomPage.Values[0]) then
  begin
    MsgBox(CustomMessage('SelectOnnx'), mbError, MB_OK);
    Result := False;
  end;
end;

// The ticked captions of a page, one per line (the summary on the "Ready to install" page).
function TickedCaptions(Page: TInputOptionWizardPage; Count: Integer; Indent, NewLine: String): String;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Count - 1 do
    if Page.Values[I] then
      Result := Result + Indent + Page.CheckListBox.ItemCaption[I] + NewLine;
  if Result = '' then
    Result := Indent + CustomMessage('MemoNone') + NewLine;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  if ModelsChosenBefore then
  begin
    Result := MemoDirInfo + NewLine + NewLine + CustomMessage('MemoModelsUnchanged');
    if MemoTasksInfo <> '' then
      Result := Result + NewLine + NewLine + MemoTasksInfo;
    Exit;
  end;
  Result := MemoDirInfo + NewLine + NewLine;
  if not IsDesktopRuntimeInstalled then
    Result := Result + FmtMessage(CustomMessage('MemoRuntime'), [DotNetMajor]) + NewLine + NewLine;
  Result := Result + CustomMessage('MemoModels') + NewLine;
  if ChoseLater then
    Result := Result + Space + CustomMessage('MemoLater') + NewLine
  else
  begin
    Result := Result +
      Space + CustomMessage('MemoAsr') + NewLine + TickedCaptions(AsrPage, AsrCount, Space + Space, NewLine) +
      Space + CustomMessage('MemoTr') + NewLine + TickedCaptions(TranslationPage, TranslationCount, Space + Space, NewLine);
    if LocalAsrTicked then
      Result := Result + Space + CustomMessage('MemoVad') + NewLine +
        Space + Space + VadPage.CheckListBox.ItemCaption[VadPage.SelectedValueIndex] + NewLine;
    Result := Result + NewLine + CustomMessage('MemoDownload');
  end;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
end;

function JsonString(Value: String): String;
begin
  StringChangeEx(Value, '\', '\\', True);
  StringChangeEx(Value, '"', '\"', True);
  Result := '"' + Value + '"';
end;

function JsonBool(Value: Boolean): String;
begin
  if Value then Result := 'true' else Result := 'false';
end;

function CustomValue(Selected: Boolean; Value: String): String;
begin
  if Selected then Result := Value else Result := '';
end;

function AsrIds: String;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to AsrCount - 1 do
    if AsrPage.Values[I] then
    begin
      if Result <> '' then Result := Result + ', ';
      Result := Result + JsonString(AsrId(I));
    end;
  Result := '[' + Result + ']';
end;

function TranslationIds: String;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to TranslationCount - 1 do
    if TranslationPage.Values[I] then
    begin
      if Result <> '' then Result := Result + ', ';
      Result := Result + JsonString(TranslationId(I));
    end;
  Result := '[' + Result + ']';
end;

// The app applies install.json once, on its first start (InstallManifest.cs). Setup runs elevated and maybe
// as another account, so it can't write the user's own settings in %APPDATA%.
// "Later" = nothing chosen, nothing to download. Translation [] = no translation. Vad '' = no local recognition.
procedure WriteInstallManifest;
var
  Json, Vad: String;
begin
  if LocalAsrTicked then Vad := VadId(VadPage.SelectedValueIndex) else Vad := '';
  Json :=
    '{' + #13#10 +
    '  "ModelsFolder": ' + JsonString(ExpandConstant('{app}\models')) + ',' + #13#10 +
    '  "Later": ' + JsonBool(ChoseLater) + ',' + #13#10 +
    '  "Asr": ' + AsrIds + ',' + #13#10 +
    '  "AsrPath": ' + JsonString(CustomValue(AsrPage.Values[AsrCustomIndex], AsrCustomPage.Values[0])) + ',' + #13#10 +
    '  "Translation": ' + TranslationIds + ',' + #13#10 +
    '  "TranslationPath": ' + JsonString(CustomValue(TranslationPage.Values[TranslationCustomIndex], TranslationCustomPage.Values[0])) + ',' + #13#10 +
    '  "Vad": ' + JsonString(Vad) + ',' + #13#10 +
    '  "VadPath": ' + JsonString(CustomValue(LocalAsrTicked and (VadPage.SelectedValueIndex = VadCustomIndex), VadCustomPage.Values[0])) + #13#10 +
    '}' + #13#10;
  if not SaveStringToFile(ExpandConstant('{app}\install.json'), Json, False) then
    Log('Could not write install.json');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // ssInstall = before files are copied: install.json from an earlier install still tells "update".
  if (CurStep = ssInstall) and not ModelsChosenBefore then
    WriteManifestAfterInstall := True;
  if (CurStep = ssPostInstall) and WriteManifestAfterInstall then
    WriteInstallManifest;
end;
#endif

{ Models are GBs and were not installed by Setup -> ask instead of leaving them silently or deleting them. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Models: String;
begin
  Models := ExpandConstant('{app}\models');
  if (CurUninstallStep = usPostUninstall) and DirExists(Models) and not UninstallSilent then
    if MsgBox(FmtMessage(CustomMessage('UninstallModels'), [Models]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(Models, True, True, True);
end;
