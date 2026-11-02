; Classic install wizard for Three-Finger Tab Scroll (Inno Setup 6).
;
; It wraps Velopack's one-click installer (ThreeFingerTabScroll-win-Setup.exe), which does the actual
; install and handles updates and uninstalling. This wizard only adds the familiar pages and the
; "Start with Windows" choice, so it registers no uninstall entry of its own: the app appears once in
; Installed apps.
;
; Build (after `vpk pack` has created releases\ThreeFingerTabScroll-win-Setup.exe):
;   ISCC.exe /DAppVersion=1.2.0 installer\ThreeFingerTabScroll.iss
; Output: releases\ThreeFingerTabScroll-Installer.exe

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "Three-Finger Tab Scroll"
#define VelopackSetup "ThreeFingerTabScroll-win-Setup.exe"

[Setup]
AppId={{6C2B7F0E-4A1D-4E6B-9C3F-2D8A5B7E1F40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=LiventNick
AppPublisherURL=https://liventnick.xyz
AppSupportURL=https://github.com/LiventNick/ThreeFingerTabScroll/issues
AppUpdatesURL=https://github.com/LiventNick/ThreeFingerTabScroll/releases
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} installer

; Per-user install, no administrator rights (same as the app itself).
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Velopack decides where the app goes and owns uninstalling, so no folder page and no second uninstall entry.
CreateAppDir=no
Uninstallable=no
DisableProgramGroupPage=yes

LicenseFile=..\LICENSE
WizardStyle=modern
SetupIconFile=..\assets\app.ico
WizardImageFile=..\assets\installer\wizard.bmp,..\assets\installer\wizard-2x.bmp
WizardSmallImageFile=..\assets\installer\wizard-small.bmp,..\assets\installer\wizard-small-2x.bmp

OutputDir=..\releases
OutputBaseFilename=ThreeFingerTabScroll-Installer
; The inner installer is already compressed; recompressing gains little.
Compression=lzma2/fast
SolidCompression=yes

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nSlide three fingers left or right on your touchpad to move between browser tabs, like on a Chromebook. Works with Chrome, Edge, Firefox, Brave, Vivaldi, Opera and other Chromium- and Firefox-based browsers.%n%nNo administrator rights are needed, and the app keeps itself up to date.
FinishedHeadingLabel=Three-Finger Tab Scroll is installed
FinishedLabelNoIcons=It runs in the system tray as a blue arrow icon (click the ^ arrow on the taskbar if you don't see it), and you can always open it from the Start menu.%n%nOne more step: turn off Windows' own three-finger swipe so the two don't clash. The app's welcome window shows you how.
FinishedLabel=It runs in the system tray as a blue arrow icon (click the ^ arrow on the taskbar if you don't see it), and you can always open it from the Start menu.%n%nOne more step: turn off Windows' own three-finger swipe so the two don't clash. The app's welcome window shows you how.

[Tasks]
Name: "autostart"; Description: "Start {#AppName} when Windows starts"

[Files]
Source: "..\releases\{#VelopackSetup}"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
; "Open app now" checkbox on the Finish page. Velopack installs the app here (per user).
Filename: "{localappdata}\ThreeFingerTabScroll\current\ThreeFingerTabScroll.exe"; Parameters: "--from-installer"; \
  Description: "Open {#AppName} now"; Flags: postinstall nowait skipifsilent skipifdoesntexist

[Code]
const
  HandoffKey = 'Software\ThreeFingerTabScroll';

// Runs Velopack's installer silently once our files are in place. It installs the app and then always
// starts it, so we first set "InstallerDeferLaunch": that copy of the app sees it, deletes it and exits,
// leaving the Finish page's "Open now" checkbox in charge.
// The "Start with Windows" choice is handed over through the registry too: the app applies it on startup,
// deletes the value, and shows its welcome window. (Passing arguments through Velopack's installer
// makes version 1.2.161 crash and hang, so don't.)
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssDone then
  begin
    // In case the app never started to pick it up, don't leave the flag behind.
    RegDeleteValue(HKCU, HandoffKey, 'InstallerDeferLaunch');
    Exit;
  end;

  if CurStep <> ssPostInstall then
    Exit;

  RegWriteDWordValue(HKCU, HandoffKey, 'InstallerStartWithWindows', Ord(WizardIsTaskSelected('autostart')));
  RegWriteDWordValue(HKCU, HandoffKey, 'InstallerDeferLaunch', 1);

  WizardForm.StatusLabel.Caption := 'Installing {#AppName}...';
  if not Exec(ExpandConstant('{tmp}\{#VelopackSetup}'), '--silent', '', SW_SHOWNORMAL,
              ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
  begin
    MsgBox('{#AppName} could not be installed (error ' + IntToStr(ResultCode) + ').' + #13#10#13#10 +
           'Please try again, or download ThreeFingerTabScroll-win-Setup.exe from the GitHub releases page instead.',
           mbError, MB_OK);
  end;
end;
