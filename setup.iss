#define MyAppName "BlackGoldAncientSword"
#ifndef MyAppVersion
#define MyAppVersion "0.0.0"
#endif
#ifndef MyAppPublisher
#define MyAppPublisher "Unknown"
#endif
#ifndef MyAppURL
#define MyAppURL "https://github.com"
#endif

[Setup]
AppId={{B8A1F3E2-9C5D-4A7B-8E6F-1D2C3A4B5F6C}}
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

[Files]
Source: "publish\Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\BlackGoldAncientSword.App.exe"; IconFilename: "{app}\BlackGoldAncientSword.Resources\Images\app.ico"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\BlackGoldAncientSword.App.exe"; IconFilename: "{app}\BlackGoldAncientSword.Resources\Images\app.ico"

[Run]
Filename: "{app}\BlackGoldAncientSword.App.exe"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent