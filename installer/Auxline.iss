; Auxline installer (Inno Setup 6). Built by tools/build-release.ps1, which passes
;   /DAppVersion=1.2.0  /DPublishDir=<the self-contained publish folder>
; Per-user install without admin rights, always to %LocalAppData%\Programs\Auxline.

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z (tools/build-release.ps1 does).
#endif
#ifndef PublishDir
  #error Pass /DPublishDir=<publish folder> (tools/build-release.ps1 does).
#endif

#define AppName "Auxline"
#define AppExe "Auxline.exe"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#define StartupApprovedKey "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"

[Setup]
; Identifies the app for upgrades and uninstalling: never change it.
AppId={{8F32B091-9067-44E7-A99F-CD2A7BB12B08}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Krzysztof Sadowy
AppPublisherURL=https://github.com/ksadowy/Auxline
AppSupportURL=https://github.com/ksadowy/Auxline/issues
AppUpdatesURL=https://github.com/ksadowy/Auxline/releases
PrivilegesRequired=lowest
; {autopf} is %LocalAppData%\Programs for a per-user install. Fixed (no folder page): updates wipe the folder first,
; which must never hit a folder the user picked for other things.
DefaultDirName={autopf}\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 11: the widget relies on its taskbar.
MinVersion=10.0.22000
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile=..\Auxline\Assets\Auxline.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; Auxline keeps running in the tray: close it for updates and uninstalling.
CloseApplications=force
RestartApplications=no

[Tasks]
Name: autostart; Description: "Start Auxline when I sign in"

[InstallDelete]
; Files of the previous version, so an update doesn't leave old libraries behind. Settings and logs live elsewhere
; (%AppData%\Auxline, %LocalAppData%\Auxline).
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
; The same entry the tray menu's "Start with Windows" writes, so the two stay in sync.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start Auxline now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop the running app first, or its files are locked and stay behind.
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#AppExe}"; Flags: runhidden; RunOnceId: "StopAuxline"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Autostart may have been turned on from the tray menu rather than by setup: remove it either way,
    // together with Task Manager's on/off state for it.
    RegDeleteValue(HKCU, '{#RunKey}', '{#AppName}');
    RegDeleteValue(HKCU, '{#StartupApprovedKey}', '{#AppName}');
  end;
end;
