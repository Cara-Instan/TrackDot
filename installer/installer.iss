; Inno Setup Script for TrackDot Windows Installer
; Compile using ISCC (Inno Setup Compiler):
; ISCC.exe /DAppVersion=0.1.0 installer/installer.iss

#ifndef AppVersion
#define AppVersion "0.1.0"
#endif

[Setup]
AppId={{D37E6B8C-5178-4A59-BF24-8F12A098E61B}
AppName=TrackDot
AppVersion={#AppVersion}
AppPublisher=Herlandro Ando
AppPublisherURL=https://github.com/herlandroando/TrackDot
AppSupportURL=https://github.com/herlandroando/TrackDot
AppUpdatesURL=https://github.com/herlandroando/TrackDot
DefaultDirName={autopf}\TrackDot
DefaultGroupName=TrackDot
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputBaseFilename=TrackDot-Setup-v{#AppVersion}-x64
OutputDir=..\release
SetupIconFile=..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\TrackDot.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostarticon"; Description: "Launch TrackDot automatically when Windows starts"; GroupDescription: "Startup Options:"

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\TrackDot"; Filename: "{app}\TrackDot.exe"
Name: "{group}\{cm:UninstallProgram,TrackDot}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\TrackDot"; Filename: "{app}\TrackDot.exe"; Tasks: desktopicon
Name: "{userstartup}\TrackDot"; Filename: "{app}\TrackDot.exe"; Tasks: autostarticon

[Run]
Filename: "{app}\TrackDot.exe"; Description: "{cm:LaunchProgram,TrackDot}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Clean up registry settings created by TrackDot
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\TrackDot');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TrackDot');
  end;
end;
