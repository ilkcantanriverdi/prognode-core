; PROGNODE Client installer (Inno Setup 6) for office and control-room PCs that receive alarm
; notifications from PROGNODE Core over the plant network. Per-user install, no admin rights needed.
; Build with installer\build.ps1.

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
AppId={{9D3C5A21-7E4B-4F0A-B6C2-1A8E5F3D7C92}
AppName=PROGNODE Client
AppVersion={#AppVersion}
AppVerName=PROGNODE Client {#AppVersion}
AppPublisher=Cordara Labs
AppPublisherURL=https://prognode.io
AppSupportURL=https://prognode.io/contact
AppUpdatesURL=https://account.prognode.io/downloads
VersionInfoVersion={#VersionInfo}
VersionInfoCompany=Cordara Labs
VersionInfoProductName=PROGNODE Client
DefaultDirName={localappdata}\Programs\PROGNODE Client
DisableDirPage=yes
DefaultGroupName=PROGNODE
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=PROGNODE-Client-Setup-{#AppVersion}
SetupIconFile=..\src\Prognode.Client.Windows\Assets\prognode.ico
UninstallDisplayIcon={app}\PROGNODE.Client.exe
UninstallDisplayName=PROGNODE Client
LicenseFile=license.txt
WizardStyle=modern
ShowLanguageDialog=no
WizardImageFile=branding\wizard-large.bmp,branding\wizard-large-200.bmp
WizardSmallImageFile=branding\wizard-small.bmp,branding\wizard-small-200.bmp
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\client\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\PROGNODE Client"; Filename: "{app}\PROGNODE.Client.exe"
Name: "{userdesktop}\PROGNODE Client"; Filename: "{app}\PROGNODE.Client.exe"; Tasks: desktopicon

[Registry]
; The client registers itself to start with Windows after pairing; remove that entry on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "PROGNODE Client"; ValueType: none; Flags: uninsdeletevalue

[Run]
Filename: "{app}\PROGNODE.Client.exe"; Description: "Start PROGNODE Client"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM PROGNODE.Client.exe"; Flags: runhidden waituntilterminated; RunOnceId: "StopPrognodeClient"

[UninstallDelete]
; Pairing token (DPAPI protected) for this Windows user.
Type: filesandordirs; Name: "{localappdata}\PROGNODE\Client"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM PROGNODE.Client.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
  Result := '';
end;
