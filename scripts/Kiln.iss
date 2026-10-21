#ifndef AppVersion
  #define AppVersion "0.2.11w"
#endif
#ifndef AppSource
  #define AppSource "..\publish\Kiln-v" + AppVersion
#endif

[Setup]
AppId={{0EFA8236-6F3B-43FA-9BE1-F1169FF45858}
AppName=Kiln
AppVersion={#AppVersion}
AppPublisher=NelsonPence
AppPublisherURL=https://github.com/Cetirzine/Kiln-Windows
AppSupportURL=https://github.com/Cetirzine/Kiln-Windows/issues
AppUpdatesURL=https://github.com/Cetirzine/Kiln-Windows/releases/latest
DefaultDirName={localappdata}\Programs\Kiln
DefaultGroupName=Kiln
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DisableProgramGroupPage=yes
WizardStyle=modern
SetupIconFile=..\Kiln.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\Kiln.exe
OutputDir=..\publish
OutputBaseFilename=Kiln-Windows-x64-v{#AppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=0.2.11.0
VersionInfoProductVersion=0.2.11.0
VersionInfoProductName=Kiln
VersionInfoDescription=Kiln for Windows Installer

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Kiln"; Filename: "{app}\Kiln.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"; AppUserModelID: "Cetirzine.Kiln"
Name: "{autodesktop}\Kiln"; Filename: "{app}\Kiln.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon; AppUserModelID: "Cetirzine.Kiln"

[Run]
Filename: "{app}\Kiln.exe"; Description: "Launch Kiln"; Flags: nowait postinstall skipifsilent unchecked
