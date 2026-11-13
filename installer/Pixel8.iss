; Pixel8 installer (Inno Setup 6). Built by build\publish.ps1 (or publish.bat), which passes /DAppVersion from
; Directory.Build.props and compiles from the self-contained publish it has just made in artifacts\app\.
; Same routine as Throng's installer. Per user, no admin rights.
;
;   Game:       %LocalAppData%\Programs\Pixel8\ (fixed, so [InstallDelete] can safely start the folder clean)
;   Shortcuts:  Start menu always; desktop as an option, unticked
;   Kept:       %LocalAppData%\Pixel8 (settings.json and the saved run) is never removed by uninstall
;
; AppId never changes: it is how a newer setup finds and upgrades an older install.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppExe "Pixel8.exe"

[Setup]
AppId={{3BA87E64-1D35-4E20-9F3A-19C8164CE697}
AppName=Pixel8
AppVersion={#AppVersion}
AppVerName=Pixel8 {#AppVersion}
AppPublisher=Gavin Nicholls
AppPublisherURL=https://github.com/aussieBIMguru/Pixel8
AppSupportURL=https://github.com/aussieBIMguru/Pixel8/issues
AppUpdatesURL=https://github.com/aussieBIMguru/Pixel8/releases
AppCopyright=Copyright (c) 2026 Gavin Nicholls (MIT)
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=Pixel8
VersionInfoDescription=Pixel8 setup
DefaultDirName={localappdata}\Programs\Pixel8
DefaultGroupName=Pixel8
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; MIT needs no acceptance: shown as an information page
InfoBeforeFile=..\LICENSE
SetupIconFile=..\src\Pixel8.Desktop\Icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=Pixel8 {#AppVersion}
OutputBaseFilename=Pixel8-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
UsePreviousTasks=yes
; Pixel8 running is checked in [Code]: it's never closed for the player
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[InstallDelete]
; The game folder holds nothing but the game: start clean (an older self-contained build may leave dlls a new one doesn't use)
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Pixel8"; Filename: "{app}\{#AppExe}"; Comment: "Pixel-art dungeon deckbuilder"
Name: "{autodesktop}\Pixel8"; Filename: "{app}\{#AppExe}"; Comment: "Pixel-art dungeon deckbuilder"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Play Pixel8"; Flags: postinstall nowait skipifsilent unchecked

[UninstallDelete]
; Only the game folder. %LocalAppData%\Pixel8 (settings and the saved run) is kept.
Type: filesandordirs; Name: "{app}"

[Code]
function IsProcessRunning(const FileName: String): Boolean;
var
  Locator, Service, Items: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Items := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = "' + FileName + '"');
    Result := Items.Count > 0;
  except
    Result := False; { can't tell: carry on (a locked file still makes setup ask) }
  end;
end;

{ Pixel8.exe holds the files this setup replaces: ask the player to close it (never closed for them) }
function Pixel8Closed(const What: String): Boolean;
begin
  Result := True;
  while IsProcessRunning('{#AppExe}') do
  begin
    if MsgBox('Please close Pixel8 before ' + What + ' (your run saves on the map).' + #13#10#13#10 +
              'Then choose Retry.', mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function InitializeSetup: Boolean;
begin
  if WizardSilent then
    Result := not IsProcessRunning('{#AppExe}')
  else
    Result := Pixel8Closed('installing');
end;

function InitializeUninstall: Boolean;
begin
  if UninstallSilent then
    Result := not IsProcessRunning('{#AppExe}')
  else
    Result := Pixel8Closed('removing it');
end;
