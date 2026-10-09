; PROGNODE Core installer (Inno Setup 6). Build with installer\build.ps1, which publishes the
; self-contained Core and tray agent first and passes AppVersion / PublishDir.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef VersionInfo
  #define VersionInfo "0.0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{6B1F2E7A-3C4D-4E8B-9A1F-5D2C8E7B4A10}
AppName=PROGNODE Core
AppVersion={#AppVersion}
AppVerName=PROGNODE Core {#AppVersion}
AppPublisher=Cordara Labs
AppPublisherURL=https://prognode.io
AppSupportURL=https://prognode.io/contact
AppUpdatesURL=https://account.prognode.io/downloads
VersionInfoVersion={#VersionInfo}
VersionInfoCompany=Cordara Labs
VersionInfoProductName=PROGNODE Core
DefaultDirName={autopf}\PROGNODE
DefaultGroupName=PROGNODE
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=PROGNODE-Core-Setup-{#AppVersion}
SetupIconFile=..\src\Prognode.Agent.Windows\Assets\prognode.ico
UninstallDisplayIcon={app}\Agent\PROGNODE.Agent.exe
UninstallDisplayName=PROGNODE Core
LicenseFile=license.txt
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Dirs]
Name: "{commonappdata}\PROGNODE\data"

[Files]
Source: "{#PublishDir}\core\*"; DestDir: "{app}\Core"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\agent\*"; DestDir: "{app}\Agent"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "scripts\install-service.ps1"; DestDir: "{app}\Setup"; Flags: ignoreversion
Source: "scripts\uninstall-service.ps1"; DestDir: "{app}\Setup"; Flags: ignoreversion
Source: "PROGNODE.url"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\PROGNODE"; Filename: "{app}\PROGNODE.url"; IconFilename: "{app}\Agent\PROGNODE.Agent.exe"
Name: "{group}\PROGNODE Notifications"; Filename: "{app}\Agent\PROGNODE.Agent.exe"
Name: "{group}\Uninstall PROGNODE Core"; Filename: "{uninstallexe}"
Name: "{autodesktop}\PROGNODE"; Filename: "{app}\PROGNODE.url"; IconFilename: "{app}\Agent\PROGNODE.Agent.exe"; Tasks: desktopicon
; Tray notifications start for every user who signs in on this PC.
Name: "{commonstartup}\PROGNODE Notifications"; Filename: "{app}\Agent\PROGNODE.Agent.exe"

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Setup\install-service.ps1"" -InstallRoot ""{app}"" -DataRoot ""{commonappdata}\PROGNODE\data"""; StatusMsg: "Starting the PROGNODE Core service..."; Flags: runhidden waituntilterminated
Filename: "{app}\Agent\PROGNODE.Agent.exe"; Description: "Start PROGNODE notifications"; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "{app}\PROGNODE.url"; Description: "Open PROGNODE"; Flags: shellexec nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Setup\uninstall-service.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "RemovePrognodeService"

[Code]
// Stop the running service and tray agent before files are replaced on an upgrade.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop PROGNODECore', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM PROGNODE.Agent.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(2000);
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    MsgBox('PROGNODE Core was removed. Your configuration, license and recorded history are kept in ' +
      ExpandConstant('{commonappdata}\PROGNODE') + ' so a reinstall continues where you left off.', mbInformation, MB_OK);
end;
