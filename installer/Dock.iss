; Inno Setup script for Dock.
; Builds a per-user installer (no admin/UAC prompt) from published output.
;
; Before compiling, publish the app as self-contained + ReadyToRun:
;   dotnet publish src\Dock.App\Dock.App.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o publish\Dock.App
;
; Then compile this script with ISCC.exe (installed via Inno Setup).

#define AppName "Dock"
#define AppVersion "1.0.0"
#define AppPublisher "Dock"
#define AppExeName "Dock.exe"

[Setup]
AppId={{7C3B6C9B-6E7B-4B7B-9D3F-2B7B7A2E6B6A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\Dock
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=Dock-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startwithwindows"; Description: "Start Dock automatically when Windows starts"; GroupDescription: "Startup:"

[Files]
Source: "..\publish\Dock.App\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Dock"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Dock"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Dock"; ValueData: """{app}\{#AppExeName}"""; Tasks: startwithwindows; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Dock"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Dock keeps two windows alive for its taskbar buttons and never exits on its own, so
    // close it before removing files -- otherwise the in-use exe blocks the uninstall.
    Exec('taskkill.exe', '/F /IM Dock.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);
  end;
end;
