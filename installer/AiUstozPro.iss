; AI Ustoz Pro — Inno Setup skripti
#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "out"
#endif

[Setup]
AppId={{B6F0A3B2-6E0C-4C9B-9A51-2F1D7E4A0C11}
AppName=AI Ustoz Pro
AppVersion={#AppVersion}
AppPublisher=Murodov M.
DefaultDirName={localappdata}\Programs\AI Ustoz Pro
DefaultGroupName=AI Ustoz Pro
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=AiUstozPro-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\AiUstozPro.exe
MinVersion=10.0

[Files]
Source: "{#PublishDir}\AiUstozPro.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\AI Ustoz Pro"; Filename: "{app}\AiUstozPro.exe"
Name: "{userdesktop}\AI Ustoz Pro"; Filename: "{app}\AiUstozPro.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Ish stolida yorliq yaratish"; GroupDescription: "Qo'shimcha:"

[Run]
Filename: "{app}\AiUstozPro.exe"; Description: "AI Ustoz Pro ni ishga tushirish"; Flags: nowait postinstall skipifsilent

; Ma'lumotlar bazasi %LOCALAPPDATA%\AiUstozPro\data ichida saqlanadi va
; dasturni o'chirish yoki yangilashda O'CHIRILMAYDI.
