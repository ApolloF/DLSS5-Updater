; Inno Setup script for the installable DLSS5 Updater. Build after `dotnet publish`:
;   iscc /DAppVersion=2.0.0 installer\DLSSUpdater.iss
; The portable build is the same single-file publish\DLSS5Updater.exe, released as is.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "DLSS5 Updater"
#define AppExe "DLSS5Updater.exe"

[Setup]
AppId={{6F0B8E53-1C2D-4B7A-9E4F-3A5D2C8B1E07}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=ApolloF
AppPublisherURL=https://github.com/ApolloF/dlss5updater
AppSupportURL=https://github.com/ApolloF/dlss5updater/issues
AppUpdatesURL=https://github.com/ApolloF/dlss5updater/releases
; Per-user by default (no admin prompt); the dialog offers an all-users install.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Not the previous group, so an upgrade from "DLSS Updater" gets the new Start menu name.
UsePreviousGroup=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupIconFile=..\src\DLSSUpdater\Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\publish
OutputBaseFilename=DLSS5Updater-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
; Same name as SingleInstance.DefaultName (kept from before the rename), so setup asks to close a running old or new copy.
; The old DLSSUpdater.exe is not in [Files], so CloseApplications alone would not catch it.
AppMutex=Local\DLSSUpdater.SingleInstance
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Leftovers of the pre-rename "DLSS Updater" install.
Type: files; Name: "{app}\DLSSUpdater.exe"
Type: files; Name: "{autodesktop}\DLSS Updater.lnk"
Type: files; Name: "{autoprograms}\DLSS Updater\DLSS Updater.lnk"
Type: dirifempty; Name: "{autoprograms}\DLSS Updater"

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Drop the old add-on link if it points at this copy. Settings and caches in %LocalAppData%\DLSS5Updater are kept.
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-addon"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterAddon"
