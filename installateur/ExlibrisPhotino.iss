; =====================================================================
;  Ex-libris (Photino) — script d'installation Inno Setup
;
;  Compilé par publier-photino.ps1, qui publie l'application juste avant.
;  Ne le compilez pas seul : il irait chercher un dossier de publication
;  peut-être périmé.
;
;  ⚠️ C'est un installateur DISTINCT de celui du WPF (Exlibris.iss), choisi
;  le 04/10/2026 : les deux versions s'installent CÔTE À CÔTE et partagent
;  la même bibliothèque (%LOCALAPPDATA%\Exlibris). La bascule viendra quand
;  la version Photino aura été validée à l'usage — ce jour-là, reprendre
;  l'AppId du WPF ici pour que l'installation le remplace.
;
;  Inno Setup s'installe une fois :
;      winget install JRSoftware.InnoSetup
; =====================================================================

#define Nom          "Ex-libris (Photino)"
#define Editeur      "Christian Sammut"
#define Source       "..\publish\photino\win-x64"
#define Executable   "Exlibris.exe"

; La version est LUE dans l'exécutable, jamais recopiée ici.
#define Version GetVersionNumbersString(Source + "\" + Executable)

[Setup]
; ⚠️ CET IDENTIFIANT NE DOIT JAMAIS CHANGER, et il DIFFÈRE de celui du WPF
; ({bb9efea3-…}) : c'est ce qui fait coexister les deux installations au lieu
; que l'une remplace l'autre.
AppId={{01b01d9a-9111-49cb-a496-681f88db189a}
AppName={#Nom}
AppVersion={#Version}
AppPublisher={#Editeur}
VersionInfoVersion={#Version}

; Installation SANS droits administrateur, comme le WPF : l'application
; n'écrit que dans les données de l'utilisateur.
PrivilegesRequired=lowest
; Un dossier distinct de celui du WPF (« Ex-libris ») : sinon les deux
; Exlibris.exe s'écraseraient l'un l'autre.
DefaultDirName={autopf}\Ex-libris Photino
DefaultGroupName={#Nom}
DisableProgramGroupPage=yes

OutputDir=.
OutputBaseFilename=Installer-Exlibris-Photino-{#Version}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

SetupIconFile=..\Exlibris\Assets\exlibris.ico
UninstallDisplayIcon={app}\{#Executable}

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "bureau"; Description: "Créer un raccourci sur le Bureau"; \
    GroupDescription: "Raccourcis :"

[Files]
; Tout le dossier publié, et non un fichier unique comme le WPF : Photino lit
; wwwroot (pages, styles, scripts) sur le disque, à côté de l'exécutable.
Source: "{#Source}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
    Excludes: "appsettings.Local.json"

; ----- Les réglages LOCAUX : clé Syncfusion, adresse de l'API Lirevia.
; Posés à la PREMIÈRE installation seulement — même raison que le WPF : ils
; diffèrent d'un poste à l'autre et personne ne les a notés ailleurs.
; ⚠️ Même réserve sur la licence Syncfusion avant toute diffusion large.
Source: "{#Source}\appsettings.Local.json"; DestDir: "{app}"; \
    Flags: onlyifdoesntexist skipifsourcedoesntexist

[Icons]
Name: "{group}\{#Nom}"; Filename: "{app}\{#Executable}"
Name: "{group}\Désinstaller {#Nom}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#Nom}"; Filename: "{app}\{#Executable}"; Tasks: bureau

[Run]
Filename: "{app}\{#Executable}"; \
    Description: "Lancer {#Nom}"; \
    Flags: nowait postinstall skipifsilent

; =====================================================================
;  ⚠️ AUCUNE SECTION [UninstallDelete], et c'est délibéré — comme le WPF.
;  La bibliothèque (%LOCALAPPDATA%\Exlibris) n'est pas installée par ce
;  programme et lui survit ; elle est d'ailleurs PARTAGÉE avec le WPF.
;
;  Prérequis : le runtime WebView2 de Microsoft, présent d'office sous
;  Windows 11 et installé par Edge sous Windows 10.
; =====================================================================
