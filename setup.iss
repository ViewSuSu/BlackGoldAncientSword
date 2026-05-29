#define MyAppName "BlackGoldAncientSword"
#define MyAppVersion "0.0.0"
#define MyAppPublisher "小窗同学"
#define MyAppURL "https://github.com/ViewSuSu/BlackGoldAncientSword"

[Setup]
AppId={{B8A1F3E2-9C5D-4A7B-8E6F-1D2C3A4B5F6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=.\output
OutputBaseFilename={#MyAppName}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
SetupIconFile=src\BlackGoldAncientSword.Resources\Images\app.ico
ShowLanguageDialog=yes
UninstallDisplayIcon={app}\BlackGoldAncientSword.App.exe

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "publish\Merged\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\BlackGoldAncientSword.App.exe"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\BlackGoldAncientSword.App.exe"; Tasks: desktopicon

[CustomMessages]
en.CreateDesktopIcon=Create a &desktop shortcut
en.AdditionalIcons=Additional icons:
chinesesimplified.CreateDesktopIcon=创建桌面快捷方式(&D)
chinesesimplified.AdditionalIcons=附加图标:

[Run]
Filename: "{app}\BlackGoldAncientSword.App.exe"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
