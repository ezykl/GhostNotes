; Inno Setup Script for GhostNotes
; Windows 11/10 OBS-Stealth Floating Sticky Notes

#define MyAppName GhostNotes
#define MyAppVersion 1.0.0
#define MyAppPublisher GhostNotes
#define MyAppExeName GhostNotes.exe

[Setup]
AppId={{D9835691-82A1-4F23-93E1-34F6A2C59D11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=GhostNotes-Setup
SetupIconFile=..\src\GhostNotes\Assets\GhostNotes.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: english; MessagesFile: compiler:Default.isl

[Tasks]
Name: desktopicon; Description: {cm:CreateDesktopIcon}; GroupDescription: {cm:AdditionalIcons}; Flags: unchecked

[Files]
Source: ..\publish\{#MyAppExeName}; DestDir: {app}; Flags: ignoreversion
Source: ..\GhostNotes.svg; DestDir: {app}; Flags: ignoreversion

[Icons]
Name: {group}\{#MyAppName}; Filename: {app}\{#MyAppExeName}; IconFilename: {app}\{#MyAppExeName}
Name: {group}\{cm:UninstallProgram,{#MyAppName}}; Filename: {uninstallexe}
Name: {autodesktop}\{#MyAppName}; Filename: {app}\{#MyAppExeName}; Tasks: desktopicon; IconFilename: {app}\{#MyAppExeName}

[Run]
Filename: {app}\{#MyAppExeName}; Description: {cm:LaunchProgram,GhostNotes}; Flags: nowait postinstall skipifsilent