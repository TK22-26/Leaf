[Setup]
AppId={{A5E8B2E1-6A56-4A07-9C6F-2A32E31C9C67}
AppName=Leaf
AppVersion={#AppVersion}
AppPublisher=Leaf
DefaultDirName={localappdata}\Leaf
DefaultGroupName=Leaf
DisableProgramGroupPage=yes
Compression=lzma2
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=Leaf-Setup-{#AppVersion}
SetupIconFile={#SourceRoot}\src\Leaf\Assets\Leaf.ico
UninstallDisplayIcon={app}\Leaf.exe
PrivilegesRequired=lowest
WizardStyle=modern

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Leaf"; Filename: "{app}\Leaf.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Leaf.exe"
