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

; The setup is in English or Ukrainian (it offers both when it starts). The program itself starts in English and has its own Language setting
; (the Language row of the settings grid), whatever language the setup was in.
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[CustomMessages]
english.RuntimeMissing={#AppName} needs the .NET 10 Desktop Runtime, which was not found on this computer.%n%nOpen the download page and exit setup? Choose No to install anyway.
ukrainian.RuntimeMissing={#AppName} потребує .NET 10 Desktop Runtime, якого не знайдено на цьому комп’ютері.%n%nВідкрити сторінку завантаження і вийти з установника? Виберіть «Ні», щоб усе одно встановити.
english.VcRuntimeMissing={#AppName} needs the Microsoft Visual C++ Redistributable (x64) 14.40 or newer for the models of the search and of the effects, and it was not found on this computer.%n%nOpen the download page and exit setup? Choose No to install anyway.
ukrainian.VcRuntimeMissing={#AppName} потребує Microsoft Visual C++ Redistributable (x64) версії 14.40 або новішої для моделей пошуку та ефектів, і його не знайдено на цьому комп’ютері.%n%nВідкрити сторінку завантаження і вийти з установника? Виберіть «Ні», щоб усе одно встановити.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; "Check for updates" in the About box runs the new setup like this:  PhotoLiker-Setup-x.exe /SILENT /Relaunch=1  (see UpdateLauncher.SetupArguments).
; A silent setup does not offer to start the program, so this entry starts it again, since the program closed itself to be replaced.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: RelaunchRequested

; The app registers its own "Open in Photo Liker" folder context menu (ContextMenuRegistry); remove it on uninstall.
; Settings and likes in %LocalAppData%\PhotoLiker are user data and are left alone.
[Registry]
Root: HKCU; Subkey: "Software\Classes\Directory\shell\OpenInPhotoLiker"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\OpenInPhotoLiker"; Flags: dontcreatekey uninsdeletekey

[Code]
// The app is framework-dependent (keeps the installer small), so it needs the .NET 10 Desktop Runtime.
const
  DesktopRuntimeKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

function HasRegisteredRuntime(RootKey: Integer): Boolean;
var
  Versions: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(RootKey, DesktopRuntimeKey, Versions) then
    for I := 0 to GetArrayLength(Versions) - 1 do
      if Pos('10.', Versions[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
end;

// The models (ONNX Runtime) need the Visual C++ runtime 14.40 or newer (x64); the redistributable of Visual Studio 2015-2022 registers its version here.
const
  VcRuntimeKey = 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64';

function IsVcRuntimeInstalled: Boolean;
var
  Installed, Minor: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM64, VcRuntimeKey, 'Installed', Installed) and (Installed = 1) then
    Result := RegQueryDWordValue(HKLM64, VcRuntimeKey, 'Minor', Minor) and (Minor >= 40);
end;

function IsDesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  // The x64 runtime installer registers itself in the 32-bit registry view (WOW6432Node), so HKLM64 alone never finds it.
  Result := HasRegisteredRuntime(HKLM32) or HasRegisteredRuntime(HKLM64);
  if Result then
    Exit;

  // Not registered (e.g. a zip install): look for the runtime folder in the default location.
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
    try
      Result := True;
    finally
      FindClose(FindRec);
    end;
end;

function RelaunchRequested: Boolean;
begin
  Result := ExpandConstant('{param:Relaunch|0}') = '1';
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDesktopRuntimeInstalled then
    if SuppressibleMsgBox(
         ExpandConstant('{cm:RuntimeMissing}'),
         mbConfirmation, MB_YESNO, IDNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
      Result := False;
    end;

  // Asked the same way: No installs anyway, and a silent setup (the update of the program runs it so) never stops here
  if Result and not IsVcRuntimeInstalled then
    if SuppressibleMsgBox(
         ExpandConstant('{cm:VcRuntimeMissing}'),
         mbConfirmation, MB_YESNO, IDNO) = IDYES then
    begin
      ShellExec('open', 'https://aka.ms/vs/17/release/vc_redist.x64.exe', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
      Result := False;
    end;
end;
