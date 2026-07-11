; BiSpell Windows installer — Inno Setup 6 (declarative)
; Compile (from repo root or any cwd; paths resolve relative to this .iss):
;   iscc /DMyAppVersion=0.2.1 windows\installer\BiSpell.iss
; Optional overrides:
;   iscc /DMyAppVersion=0.2.1 /DSourceDir=..\..\dist\BiSpell-win-x64 /DOutputDir=..\..\dist windows\installer\BiSpell.iss
;
; Payload is the unpackaged self-contained tree from package-release.ps1
; (dist\BiSpell-win-x64\ with BiSpell.App.exe, bispell_core.dll, Dictionaries\, …).

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\dist\BiSpell-win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

#define MyAppName "BiSpell"
#define MyAppPublisher "BiSpell"
#define MyAppURL "https://github.com/bispell/bispell"
#define MyAppExeName "BiSpell.App.exe"

[Setup]
; Fixed AppId — do not change; upgrades find the previous install.
; Leading brace doubled so Inno emits a literal {GUID}.
AppId={{B15BE110-A001-4F0D-9E11-B15BE1100001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Admin default; wizard may offer non-admin override (per-user under {localappdata}\Programs when allowed).
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=BiSpell-{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
; User data lives under %APPDATA%\BiSpell\ (settings, lexicon, notes).
; Uninstall intentionally does NOT remove that folder — no [UninstallDelete] for AppData.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
; Default finish text. When the taskbar task is checked, [Code] expands this with pin-yourself steps.
FinishedLabel=Setup has finished installing [name] on your computer.%n%nA Start Menu shortcut was added.

[Tasks]
; Default unchecked: desktop shortcut only when the user opts in.
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
; Honest taskbar option: cannot auto-pin on modern Windows; guides the user (see FinishedLabel).
Name: "taskbar"; Description: "Show how to &pin BiSpell to the taskbar (not automatic — pin yourself from the Start Menu after install)"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Recursive copy of the published tree into {app}.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu always (not gated by a task).
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
; Desktop only when desktopicon task is checked.
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Third wizard option: launch after install (checked once by default; skipped in silent installs).
; [Run] has no checkedonce (that is a [Tasks] flag). Default checked; user can uncheck.
Filename: "{app}\{#MyAppExeName}"; Description: "Start {#MyAppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

; Minimal [Code]: only adjusts FinishedLabel when the honest taskbar task is checked.
; No COM pin, no shell automation — guidance text only.
[Code]
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    if WizardIsTaskSelected('taskbar') then
    begin
      WizardForm.FinishedLabel.Caption :=
        'Setup has finished installing BiSpell on your computer.' + #13#10 + #13#10 +
        'A Start Menu shortcut was added. To pin BiSpell to the taskbar: open the Start Menu, ' +
        'find BiSpell, right-click it, then choose "Pin to taskbar". ' +
        'Windows does not allow installers to guarantee a taskbar pin — this is a manual step.';
    end;
  end;
end;
