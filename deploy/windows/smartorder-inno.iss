#ifndef SourceDir
  #define SourceDir "D:\eyesmanagement\pc\bin\Debug\net6.0-windows"
#endif

#ifndef OutputDir
  #define OutputDir "D:\eyesmanagement\deploy\windows\output"
#endif

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{8D75D386-71B0-49DA-9840-260A0246C64B}
AppName=SmartOrder
AppVerName=SmartOrder {#AppVersion}
AppVersion={#AppVersion}
AppPublisher=SmartOrder
VersionInfoVersion={#AppVersion}
VersionInfoCompany=SmartOrder
VersionInfoDescription=SmartOrder Installer
VersionInfoProductName=SmartOrder
DefaultDirName={autopf32}\SmartOrder
DefaultGroupName=SmartOrder
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=SmartOrder-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x86compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\SmartOrder.exe
SetupLogging=yes
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\SmartOrder"; Filename: "{app}\SmartOrder.exe"
Name: "{autodesktop}\SmartOrder"; Filename: "{app}\SmartOrder.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SmartOrder.exe"; Description: "启动 SmartOrder"; Flags: nowait postinstall skipifsilent
