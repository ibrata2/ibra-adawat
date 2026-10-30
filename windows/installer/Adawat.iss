; IBMC — Ibrahim Multimedia Converter (based on Adawat by Motalim, GPL v3) for Windows: a per-user installer (no administrator rights), in Arabic or English.
; Built by windows/tools/build-installer.sh, which publishes the app and passes:
;   /DArch=arm64|x64  /DSource=<published folder>  /DAppVersion=1.0.0

#ifndef Arch
  #define Arch "arm64"
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Source
  #define Source "..\build\publish\app-" + Arch
#endif

[Setup]
AppId={{8D4E2B71-3C9A-4F6E-9B15-1A7C0E5D93F2}
AppName=IBMC
AppVersion={#AppVersion}
AppVerName=IBMC {#AppVersion}
AppPublisher=Ibrahim Rosem
AppPublisherURL=https://github.com/ibrata2/ibra-adawat
AppSupportURL=https://github.com/ibrata2/ibra-adawat/issues
AppUpdatesURL=https://github.com/ibrata2/ibra-adawat/releases
AppCopyright=IBMC · Ibrahim Rosem · based on Adawat by Motalim · GPL v3
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\IBMC
UsePreviousAppDir=no
DefaultGroupName=IBMC
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
; No license page: the GPL needs no acceptance to install or use; LICENSE.txt is installed with the app.
SetupIconFile=..\assets\Adawat.ico
WizardImageFile=wizard-side-100.bmp,wizard-side-150.bmp,wizard-side-200.bmp
WizardSmallImageFile=wizard-corner-100.bmp,wizard-corner-150.bmp,wizard-corner-200.bmp
UninstallDisplayIcon={app}\IBMC.exe
UninstallDisplayName=IBMC — Ibrahim Multimedia Converter
OutputDir=..\build\installer
; A fixed name keeps the "latest release" download links valid from one version to the next.
OutputBaseFilename=IBMC-Setup-{#Arch}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
ShowLanguageDialog=auto
LanguageDetectionMethod=none
MinVersion=10.0.17763
; A running Adawat locks its files; PrepareToInstall below closes it before anything is copied.
CloseApplications=no
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Languages]
Name: "ar"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
en.StartWithWindows=Start IBMC when Windows starts
ar.StartWithWindows=تشغيل IBMC مع بدء Windows
en.DesktopIcon=Create a desktop shortcut
ar.DesktopIcon=إنشاء اختصار على سطح المكتب
en.Extras=Options:
ar.Extras=خيارات:
en.Launch=Start IBMC
ar.Launch=تشغيل IBMC

[Tasks]
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:Extras}"
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Extras}"

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Excludes: "*.pdb,Microsoft.Web.WebView2.*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; IBRA Adawat, the earlier name of this app: its folder and shortcuts.
Type: filesandordirs; Name: "{autopf}\IBRA Adawat"
Type: files; Name: "{autodesktop}\IBRA أدوات.lnk"
Type: files; Name: "{autoprograms}\IBRA أدوات.lnk"
; Files from an earlier version that this one no longer ships.
Type: filesandordirs; Name: "{app}\ar"
Type: files; Name: "{app}\*.pdb"
Type: files; Name: "{app}\Microsoft.Web.WebView2.*.xml"

[Icons]
Name: "{autoprograms}\IBMC"; Filename: "{app}\IBMC.exe"
Name: "{autodesktop}\IBMC"; Filename: "{app}\IBMC.exe"; Tasks: desktopicon

[Registry]
; Entries of IBRA Adawat, the earlier name.
Root: HKCU; Subkey: "Software\Classes\*\shell\IbraAdawat"; Flags: deletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\IbraAdawat"; Flags: deletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "IbraAdawat"; Flags: deletevalue
; The same value the app's own "Start Adawat when Windows starts" setting writes.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "IBMC"; ValueData: """{app}\IBMC.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue

; Right-click menu for files and folders (the app also refreshes these each time it starts).
Root: HKCU; Subkey: "Software\Classes\*\shell\IBMC"; ValueType: string; ValueName: "MUIVerb"; ValueData: "IBMC"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\IBMC"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\IBMC.exe"",0"
Root: HKCU; Subkey: "Software\Classes\*\shell\IBMC"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCU; Subkey: "Software\Classes\*\shell\IBMC\command"; ValueType: string; ValueName: ""; ValueData: """{app}\IBMC.exe"" --open ""%1"""
Root: HKCU; Subkey: "Software\Classes\Directory\shell\IBMC"; ValueType: string; ValueName: "MUIVerb"; ValueData: "IBMC"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\IBMC"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\IBMC.exe"",0"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\IBMC"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\IBMC\command"; ValueType: string; ValueName: ""; ValueData: """{app}\IBMC.exe"" --open ""%1"""

[Run]
Filename: "{app}\IBMC.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent
; An update from inside IBMC runs this installer silently; start IBMC again when it is done.
Filename: "{app}\IBMC.exe"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/im IBMC.exe /f"; Flags: runhidden; RunOnceId: "StopIBMC"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
{ Closes a running Adawat (and its command line) so every file can be replaced; it holds no unsaved work. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im IBMC.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im IbraAdawat.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im ibmc-cli.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
  Result := '';
end;

{ A first install in Arabic starts the app in Arabic too; later installs keep the language chosen in Settings. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  Folder: String;
begin
  if CurStep = ssPostInstall then
  begin
    Folder := ExpandConstant('{userappdata}\IBMC');
    { Settings and downloaded models of IBRA Adawat, the earlier name, move over. }
    if (not DirExists(Folder)) and DirExists(ExpandConstant('{userappdata}\IbraAdawat')) then
      RenameFile(ExpandConstant('{userappdata}\IbraAdawat'), Folder);
    if not FileExists(Folder + '\settings.json') then
    begin
      ForceDirectories(Folder);
      SaveStringToFile(Folder + '\settings.json', '{"Language":"' + ActiveLanguage + '"}', False);
    end;
  end;
end;
