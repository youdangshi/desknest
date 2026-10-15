; 桌巢 DeskNest 安装脚本（Inno Setup 6）
; 编译前先生成自包含发布：dotnet publish src\DeskNest.App\DeskNest.App.csproj -c Release -r win-x64 --self-contained true -o dist\DeskNest-1.0.0

#define MyAppName "桌巢 DeskNest"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "DeskNest"
#define MyAppExeName "DeskNest.exe"

[Setup]
AppId={{8C1D5B62-5B1E-4E2A-9C31-7A4E9D2F6B10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\DeskNest
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=DeskNest-Setup-{#MyAppVersion}-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 普通权限运行：管理员权限下无法从资源管理器拖放文件
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinese"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"
Name: "autostart"; Description: "开机自动启动（可在设置里关闭）"; GroupDescription: "附加任务:"

[Files]
Source: "..\dist\DeskNest-1.0.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DeskNest"; \
  ValueData: """{app}\{#MyAppExeName}"" --silent"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 只删程序目录，用户数据保留在 %LOCALAPPDATA%\DeskNest（安装包不碰用户数据）
Type: filesandordirs; Name: "{app}"
