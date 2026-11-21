#ifndef ReleaseDir
#error Supply the assembled Windows release folder with /DReleaseDir.
#endif
#ifndef ReleaseVersion
#define ReleaseVersion "0.1.0"
#endif

[Setup]
AppId={{0CCCC5B9-30CE-4A92-8D94-BA9B5598D86F}
AppName=Gromnie Grand Prix
AppVersion={#ReleaseVersion}
AppPublisher=Derpy
VersionInfoVersion={#ReleaseVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Gromnie Grand Prix
DefaultGroupName=Gromnie Grand Prix
DisableProgramGroupPage=yes
AllowNoIcons=yes
UninstallDisplayIcon={app}\GromnieGrandPrix.exe
SetupIconFile=..\..\GromnieGrandPrix\Data\GromnieGrandPrix.ico
InfoBeforeFile=Asherons Call DAT Setup.txt
OutputBaseFilename=Gromnie-Grand-Prix-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#ReleaseDir}\GromnieGrandPrix.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\GromnieGrandPrix.pck"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDir}\data_GromnieGrandPrix_windows_x86_64\*"; DestDir: "{app}\data_GromnieGrandPrix_windows_x86_64"; Flags: ignoreversion recursesubdirs
Source: "{#ReleaseDir}\Soundtrack\*.mp3"; DestDir: "{app}\Soundtrack"; Flags: ignoreversion
Source: "{#ReleaseDir}\Soundtrack\Readme.txt"; DestDir: "{app}\Soundtrack"; Flags: ignoreversion
Source: "{#ReleaseDir}\ThirdPartyNotices\*"; DestDir: "{app}\ThirdPartyNotices"; Flags: ignoreversion recursesubdirs
Source: "{#ReleaseDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "Asherons Call DAT Setup.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Gromnie Grand Prix"; Filename: "{app}\GromnieGrandPrix.exe"; WorkingDir: "{app}"
Name: "{group}\Asheron's Call DAT Setup"; Filename: "{app}\Asherons Call DAT Setup.txt"
Name: "{autodesktop}\Gromnie Grand Prix"; Filename: "{app}\GromnieGrandPrix.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\GromnieGrandPrix.exe"; Description: "Launch Gromnie Grand Prix"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
