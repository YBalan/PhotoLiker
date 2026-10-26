; Inno Setup script for Photo Liker (WinForms app, PhotoLikerUI).
; Built by installer\build-installer.ps1, which passes /DAppVersion, /DSourceDir and /DOutputDir.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\PhotoLikerUI"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish\installer"
#endif

#define AppName "Photo Liker"
#define AppExe "PhotoLikerUI.exe"

[Setup]
AppId={{6F2B8C1E-4D3A-4F6B-9A57-2C8E1D0B7A94}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Yurii Balan
AppPublisherURL=https://github.com/YBalan/PhotoLiker
; Per-user install: no UAC prompt, and the app keeps its settings and context-menu entries under HKCU / %LocalAppData%.
PrivilegesRequired=lowest
DefaultDirName={autopf}\PhotoLiker
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\PhotoLikerUI\PhotoLiker.ico
UninstallDisplayIcon={app}\{#AppExe}
OutputDir={#OutputDir}
OutputBaseFilename=PhotoLiker-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; The app registers its own "Open in Photo Liker" folder context menu (ContextMenuRegistry); remove it on uninstall.
; Settings and likes in %LocalAppData%\PhotoLiker are user data and are left alone.
[Registry]
Root: HKCU; Subkey: "Software\Classes\Directory\shell\OpenInPhotoLiker"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\OpenInPhotoLiker"; Flags: dontcreatekey uninsdeletekey

[Code]
// The app is framework-dependent (keeps the installer small), so it needs the .NET 10 Desktop Runtime.
function IsDesktopRuntimeInstalled: Boolean;
var
  Versions: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Versions) then
    for I := 0 to GetArrayLength(Versions) - 1 do
      if Pos('10.', Versions[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDesktopRuntimeInstalled then
    if SuppressibleMsgBox(
         '{#AppName} needs the .NET 10 Desktop Runtime, which was not found on this computer.' + #13#10 + #13#10 +
         'Open the download page and exit setup? Choose No to install anyway.',
         mbConfirmation, MB_YESNO, IDNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
      Result := False;
    end;
end;
