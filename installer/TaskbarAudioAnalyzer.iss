#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif

[Setup]
AppId={{80BAAF16-B154-4E74-AC37-839A899B38DD}
AppName=Taskbar Audio Analyzer
AppVersion={#AppVersion}
AppPublisher=12sound
AppPublisherURL=https://github.com/twelvesound/TaskbarAudioAnalyzer
DefaultDirName={autopf}\TaskbarAudioAnalyzer
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=TaskbarAudioAnalyzer-v{#AppVersion}-win-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\TaskbarAudioAnalyzer.exe
AppMutex=Local\12sound.TaskbarAudioAnalyzer
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
japanese.RuntimeRequired=.NET 10 Desktop Runtime (Windows x64) が必要です。https://dotnet.microsoft.com/download/dotnet/10.0 からインストール後、もう一度セットアップを実行してください。
english.RuntimeRequired=.NET 10 Desktop Runtime (Windows x64) is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and run Setup again.
japanese.CloseAnalyzer=Taskbar Audio Analyzer のメニューから Exit を選択して終了し、セットアップを続行してください。
english.CloseAnalyzer=Choose Exit in the Taskbar Audio Analyzer menu, then continue Setup.
japanese.LaunchAnalyzer=Taskbar Audio Analyzer を起動する
english.LaunchAnalyzer=Launch Taskbar Audio Analyzer

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,settings.json"

[Icons]
Name: "{commonprograms}\Taskbar Audio Analyzer"; Filename: "{app}\TaskbarAudioAnalyzer.exe"; WorkingDir: "{app}"

[Run]
; Preserve existing startup choice in the initiating user's profile, not the UAC administrator's.
Filename: "{app}\TaskbarAudioAnalyzer.exe"; Parameters: "--configure-install"; Flags: runasoriginaluser waituntilterminated
Filename: "{app}\TaskbarAudioAnalyzer.exe"; Description: "{cm:LaunchAnalyzer}"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
function HasDesktopRuntime: Boolean;
var
  Versions: TArrayOfString;
  I: Integer;
begin
  Result := False;
  { .NET registers its x64 runtime inventory in the 32-bit registry view. }
  if RegGetValueNames(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Versions) then
    for I := 0 to GetArrayLength(Versions) - 1 do
      if (Pos('10.0.', Versions[I]) = 1) and (Pos('-', Versions[I]) = 0) then
        Result := True;
end;

function InitializeSetup: Boolean;
begin
  Result := HasDesktopRuntime;
  if not Result then
    SuppressibleMsgBox(CustomMessage('RuntimeRequired'), mbError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Locator, Services, Processes: Variant;
begin
  Result := '';
  { Covers the pre-installer v1.1.0 build, which did not create an AppMutex. }
  { A title lookup can match Setup's own hidden application window. }
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Services.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''TaskbarAudioAnalyzer.exe''');
    if Processes.Count > 0 then
      Result := CustomMessage('CloseAnalyzer');
  except
    Result := GetExceptionMessage;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupPath: String;
  Command: AnsiString;
begin
  if CurUninstallStep = usUninstall then begin
    StartupPath := ExpandConstant('{userstartup}\TaskbarAudioAnalyzer.cmd');
    { Only remove a startup entry that points to this installation. Keep user settings. }
    if LoadStringFromFile(StartupPath, Command) then
      if Pos(Lowercase(ExpandConstant('{app}\TaskbarAudioAnalyzer.exe')), Lowercase(String(Command))) > 0 then
        DeleteFile(StartupPath);
  end;
end;
