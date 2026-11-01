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

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start Voxa when Windows starts"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

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
    if SuppressibleMsgBox('Voxa is running. Close it (tray icon → Exit) and click Retry.',
      mbError, MB_RETRYCANCEL, IDCANCEL) = IDCANCEL then
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
    'Downloading the .NET ' + DotNetMajor + ' Desktop Runtime from Microsoft (about 60 MB)…', nil);
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
        // (#13#10 must not start a line: ISPP would read it as a preprocessor directive)
        SuppressibleMsgBox('The .NET Desktop Runtime could not be downloaded: ' + GetExceptionMessage + #13#10#13#10 +
          'Check the internet connection, or install it from https://dotnet.microsoft.com/download/dotnet/' +
          DotNetMajor + '.0 and run Setup again.', mbCriticalError, MB_OK, IDOK);
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
  WizardForm.PreparingLabel.Caption := 'Installing the .NET ' + DotNetMajor + ' Desktop Runtime…';
  if not Exec(ExpandConstant('{tmp}\' + DotNetInstallerFile), '/install /quiet /norestart', '',
    SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    Result := 'The .NET Desktop Runtime installer could not be started.'
  else if ResultCode = 3010 then
    NeedsRestart := True // installed, Windows wants a restart
  else if ResultCode <> 0 then
    Result := 'The .NET Desktop Runtime installation failed (exit code ' + IntToStr(ResultCode) + ').';
end;

#if ModelSelectionPage
var
  AsrPage, TranslationPage, VadPage: TInputOptionWizardPage;
  AsrCustomPage: TInputDirWizardPage;
  TranslationCustomPage, VadCustomPage: TInputFileWizardPage;
  DownloadPage: TDownloadWizardPage;
  DownloadCount: Integer;
  WriteManifestAfterInstall: Boolean;

{ Ids must match ModelOffers.cs in the app. Index = order of the radio buttons below. }
function AsrId(Index: Integer): String;
begin
  case Index of
    0: Result := 'parakeet-tdt-0.6b-v3';
    1: Result := 'whisper-large-v3-turbo';
    2: Result := 'custom-asr';
  else
    Result := 'cloud-asr';
  end;
end;

function TranslationId(Index: Integer): String;
begin
  case Index of
    0: Result := 'hy-mt2-1.8b';
    1: Result := 'deepl';
    2: Result := 'azure';
    3: Result := 'cloud-translation';
    4: Result := 'custom-translation';
  else
    Result := 'no-translation';
  end;
end;

function VadId(Index: Integer): String;
begin
  if Index = 0 then Result := 'silero-vad' else Result := 'custom-vad';
end;

const
  AsrCustomIndex = 2;
  TranslationCustomIndex = 4;
  VadCustomIndex = 1;

procedure InitializeWizard;
begin
  AsrPage := CreateInputOptionPage(wpSelectTasks,
    'Speech recognition', 'Which model should turn the audio into text?',
    'The recommended model is preselected. Everything can be changed later in Voxa Settings.', True, False);
  AsrPage.Add('Parakeet TDT 0.6B v3  (local, ≈ 670 MB, recommended)');
  AsrPage.Add('Whisper large-v3 turbo  (local, ≈ 1.0 GB, 99 languages, slower)');
  AsrPage.Add('Own model  (a sherpa-onnx folder on this PC)');
  AsrPage.Add('Voxa Cloud  (coming soon)');
  AsrPage.SelectedValueIndex := 0;

  AsrCustomPage := CreateInputDirPage(AsrPage.ID,
    'Own speech recognition model', 'Where is the model?',
    'Select the folder with the encoder / decoder .onnx files and tokens.txt.', False, '');
  AsrCustomPage.Add('');

  TranslationPage := CreateInputOptionPage(AsrCustomPage.ID,
    'Translation', 'How should Voxa translate?',
    'Finished sentences are translated into your language.', True, False);
  TranslationPage.Add('Hy-MT2 1.8B  (local on the GPU, ≈ 1.9 GB, recommended)');
  TranslationPage.Add('DeepL API  (your own key, entered in Voxa Settings)');
  TranslationPage.Add('Azure Translator  (your own key, entered in Voxa Settings)');
  TranslationPage.Add('Voxa Cloud  (coming soon)');
  TranslationPage.Add('Own model  (a .gguf file on this PC)');
  TranslationPage.Add('No translation  (only the transcript)');
  TranslationPage.SelectedValueIndex := 0;

  TranslationCustomPage := CreateInputFilePage(TranslationPage.ID,
    'Own translation model', 'Where is the model?', 'Select a .gguf translation model.');
  TranslationCustomPage.Add('Model file:', 'GGUF model (*.gguf)|*.gguf|All files|*.*', '.gguf');

  VadPage := CreateInputOptionPage(TranslationCustomPage.ID,
    'Voice activity detection', 'Which model should find where sentences start and end?',
    'Keep the default unless you know why.', True, False);
  VadPage.Add('Silero VAD  (≈ 2 MB, recommended)');
  VadPage.Add('Own model  (an .onnx file on this PC)');
  VadPage.SelectedValueIndex := 0;

  VadCustomPage := CreateInputFilePage(VadPage.ID,
    'Own voice activity detection model', 'Where is the model?', 'Select a VAD .onnx model.');
  VadCustomPage.Add('Model file:', 'ONNX model (*.onnx)|*.onnx|All files|*.*', '.onnx');

  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), 'Downloading the selected models…', nil);
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

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  // Nested ifs, not "and": {app} is unknown before the directory page, ModelsChosenBefore must not run there.
  if IsModelPage(PageID) then
    if ModelsChosenBefore then
    begin
      Result := True;
      Exit;
    end;
  Result :=
    ((PageID = AsrCustomPage.ID) and (AsrPage.SelectedValueIndex <> AsrCustomIndex)) or
    ((PageID = TranslationCustomPage.ID) and (TranslationPage.SelectedValueIndex <> TranslationCustomIndex)) or
    ((PageID = VadCustomPage.ID) and (VadPage.SelectedValueIndex <> VadCustomIndex));
end;

{ The app downloads the chosen models on its first start (ids from install.json, see ModelDownloads.cs),
  so nothing is added here and the download page is skipped. Kept for a possible download in the installer. }
procedure AddModelDownloads(Id: String);
begin
  // TODO(next step): every file of the model, downloaded into the temp folder and moved to models\ in ssPostInstall, e.g.
  //   DownloadPage.Add('https://huggingface.co/<org>/<repo>/resolve/main/encoder.int8.onnx', 'encoder.int8.onnx', '<sha256>');
  //   DownloadCount := DownloadCount + 1;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if (CurPageID = AsrCustomPage.ID) and not DirExists(AsrCustomPage.Values[0]) then
  begin
    MsgBox('Select an existing folder.', mbError, MB_OK);
    Result := False;
  end
  else if (CurPageID = TranslationCustomPage.ID) and not FileExists(TranslationCustomPage.Values[0]) then
  begin
    MsgBox('Select an existing .gguf file.', mbError, MB_OK);
    Result := False;
  end
  else if (CurPageID = VadCustomPage.ID) and not FileExists(VadCustomPage.Values[0]) then
  begin
    MsgBox('Select an existing .onnx file.', mbError, MB_OK);
    Result := False;
  end
  else if (CurPageID = wpReady) and not ModelsChosenBefore then
  begin
    DownloadPage.Clear;
    DownloadCount := 0;
    AddModelDownloads(AsrId(AsrPage.SelectedValueIndex));
    AddModelDownloads(TranslationId(TranslationPage.SelectedValueIndex));
    AddModelDownloads(VadId(VadPage.SelectedValueIndex));
    if DownloadCount > 0 then
    begin
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
        except
          if DownloadPage.AbortedByUser then
            Log('Model download aborted by the user.')
          else
            SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
          Result := False;
        end;
      finally
        DownloadPage.Hide;
      end;
    end;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  if ModelsChosenBefore then
  begin
    Result := MemoDirInfo + NewLine + NewLine + 'Models: unchanged (update)';
    if MemoTasksInfo <> '' then
      Result := Result + NewLine + NewLine + MemoTasksInfo;
    Exit;
  end;
  Result := MemoDirInfo + NewLine + NewLine;
  if not IsDesktopRuntimeInstalled then
    Result := Result + '.NET ' + DotNetMajor + ' Desktop Runtime: will be downloaded and installed' + NewLine + NewLine;
  Result := Result +
    'Models:' + NewLine +
    Space + 'Speech recognition: ' + AsrPage.CheckListBox.ItemCaption[AsrPage.SelectedValueIndex] + NewLine +
    Space + 'Translation: ' + TranslationPage.CheckListBox.ItemCaption[TranslationPage.SelectedValueIndex] + NewLine +
    Space + 'Voice activity: ' + VadPage.CheckListBox.ItemCaption[VadPage.SelectedValueIndex] + NewLine +
    Space + '(downloading is not implemented yet: put the model files into ' + ExpandConstant('{app}\models') + ')';
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
end;

function JsonString(Value: String): String;
begin
  StringChangeEx(Value, '\', '\\', True);
  StringChangeEx(Value, '"', '\"', True);
  Result := '"' + Value + '"';
end;

function CustomValue(Selected, CustomIndex: Integer; Value: String): String;
begin
  if Selected = CustomIndex then Result := Value else Result := '';
end;

// The app applies install.json once, on its first start (InstallManifest.cs). Setup runs elevated and maybe
// as another account, so it can't write the user's own settings in %APPDATA%.
procedure WriteInstallManifest;
var
  Json: String;
begin
  Json :=
    '{' + #13#10 +
    '  "ModelsFolder": ' + JsonString(ExpandConstant('{app}\models')) + ',' + #13#10 +
    '  "Asr": ' + JsonString(AsrId(AsrPage.SelectedValueIndex)) + ',' + #13#10 +
    '  "AsrPath": ' + JsonString(CustomValue(AsrPage.SelectedValueIndex, AsrCustomIndex, AsrCustomPage.Values[0])) + ',' + #13#10 +
    '  "Translation": ' + JsonString(TranslationId(TranslationPage.SelectedValueIndex)) + ',' + #13#10 +
    '  "TranslationPath": ' + JsonString(CustomValue(TranslationPage.SelectedValueIndex, TranslationCustomIndex, TranslationCustomPage.Values[0])) + ',' + #13#10 +
    '  "Vad": ' + JsonString(VadId(VadPage.SelectedValueIndex)) + ',' + #13#10 +
    '  "VadPath": ' + JsonString(CustomValue(VadPage.SelectedValueIndex, VadCustomIndex, VadCustomPage.Values[0])) + #13#10 +
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
    if MsgBox('Also delete the models in ' + Models + '?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(Models, True, True, True);
end;
