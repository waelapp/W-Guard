; W-Guard installer (Inno Setup 6)
; © 2026 WAEL - https://github.com/waelapp - GPL-3.0
; Built by build.bat after W-Guard.exe is compiled.

#define AppName "W-Guard"
#define AppExe "W-Guard.exe"
#define AppVer GetStringFileInfo(SourcePath + "..\W-Guard.exe", "ProductVersion")
#define AppFileVer GetVersionNumbersString(SourcePath + "..\W-Guard.exe")

[Setup]
AppId={{435C97FD-AF71-41A8-AF6D-400F302AD268}
AppName={#AppName}
AppVersion={#AppVer}
AppVerName={#AppName} {#AppVer}
AppPublisher=WAEL
AppPublisherURL=https://github.com/waelapp
AppSupportURL=https://github.com/waelapp/W-Guard/issues
AppUpdatesURL=https://github.com/waelapp/W-Guard/releases
AppCopyright=© 2026 WAEL - GPL-3.0
VersionInfoVersion={#AppFileVer}
VersionInfoCompany=WAEL
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..
OutputBaseFilename=W-Guard-Setup
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=wizard.bmp,wizard-2x.bmp
WizardSmallImageFile=wizard-small.bmp,wizard-small-2x.bmp
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; closes a running W-Guard before installing/updating (same name as the app's single-instance mutex)
AppMutex=W-Guard.WAEL.SingleInstance
CloseApplications=yes
ShowLanguageDialog=auto

[Languages]
#if FileExists(SourcePath + "Arabic.isl")
Name: "ar"; MessagesFile: "Arabic.isl"
#elif FileExists(CompilerPath + "Languages\Arabic.isl")
Name: "ar"; MessagesFile: "compiler:Languages\Arabic.isl"
#endif
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\W-Guard.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\TRADEMARK.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; normal install: optional "Launch W-Guard" checkbox on the last page
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent shellexec
; silent install = in-app update: start the new version automatically
Filename: "{app}\{#AppExe}"; Flags: nowait shellexec; Check: WizardSilent

[UninstallDelete]
Type: files; Name: "{app}\W-Guard.exe.old"
Type: files; Name: "{app}\W-Guard.exe.download"
