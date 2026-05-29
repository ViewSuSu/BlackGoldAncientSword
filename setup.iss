#define MyAppName "BlackGoldAncientSword"
#define ExePath "publish\Release\BlackGoldAncientSword.App.exe"

#ifndef MyAppVersion
  #define MyAppVersion GetFileVersion(ExePath)
#endif

#define MyAppPublisher GetStringFileInfo(ExePath, "CompanyName")
#define MyAppURL GetStringFileInfo(ExePath, "FileDescription")

[Setup]
AppId={{B8A1F3E2-9C5D-4A7B-8E6F-1D2C3A4B5F6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=.\output
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=src\BlackGoldAncientSword.Resources\Images\app.ico
UninstallDisplayIcon={app}\BlackGoldAncientSword.App.exe

[Files]
Source: "publish\Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\BlackGoldAncientSword.App.exe"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\BlackGoldAncientSword.App.exe"

[Run]
Filename: "{app}\BlackGoldAncientSword.App.exe"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent