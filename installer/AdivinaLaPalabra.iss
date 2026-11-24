#ifndef APP_ARCH
  #define APP_ARCH "x64"
#endif
#define AppName "Adivina la Palabra con 2 Fotos"
#define PublishDir "..\\publish\\" + APP_ARCH
[Setup]
AppId={{C92D93D1-5B18-4C1D-A8B9-AD1A9A1A2A10}
AppName={#AppName}
AppVersion=1.0.0
AppPublisher=Desafíos Locos
DefaultDirName={autopf}\\Desafios Locos\\Adivina la Palabra
DefaultGroupName=Desafíos Locos\\Adivina la Palabra
OutputDir=..\\installer-output
OutputBaseFilename=Instalador_AdivinaPalabra_{#APP_ARCH}
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
#if APP_ARCH == "x64"
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
#else
ArchitecturesAllowed=x86
#endif
[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\\Spanish.isl"
[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked
[Files]
Source: "{#PublishDir}\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\\Adivina la Palabra con 2 Fotos"; Filename: "{app}\\AdivinaLaPalabra.exe"
Name: "{autodesktop}\\Adivina la Palabra con 2 Fotos"; Filename: "{app}\\AdivinaLaPalabra.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\\AdivinaLaPalabra.exe"; Description: "Iniciar el juego"; Flags: postinstall nowait skipifsilent