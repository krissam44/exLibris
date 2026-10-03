; =====================================================================
;  Ex-libris — script d'installation Inno Setup
;
;  Compilé par publier.ps1, qui publie l'application juste avant. Ne le
;  compilez pas seul : il irait chercher un dossier « publish\compresse »
;  peut-être périmé, et livrerait la version d'avant-hier sans le dire.
;
;  Inno Setup s'installe une fois :
;      winget install JRSoftware.InnoSetup
; =====================================================================

#define Nom          "Ex-libris"
#define Editeur      "Christian Sammut"
#define Source       "..\publish\compresse"
#define Executable   "Exlibris.exe"

; La version est LUE dans l'exécutable, jamais recopiée ici. Deux endroits
; où l'inscrire finiraient par se contredire, et c'est l'installateur qui
; aurait tort — celui qui compare les numéros pour décider d'une mise à jour.
#define Version GetVersionNumbersString(Source + "\" + Executable)

[Setup]
; ⚠️ CET IDENTIFIANT NE DOIT JAMAIS CHANGER. C'est lui, et non le nom, qui
; permet à Windows de reconnaître une version déjà installée : la remplacer
; au lieu d'en poser une seconde à côté, et proposer une seule entrée dans
; « Applications installées ». Le modifier créerait un doublon indésinstallable
; proprement.
AppId={{bb9efea3-4127-4a95-b21b-7ef5dd29683f}
AppName={#Nom}
AppVersion={#Version}
AppPublisher={#Editeur}
VersionInfoVersion={#Version}

; ----- Installation SANS droits administrateur.
;
; « lowest » évite la fenêtre jaune de l'UAC : l'application atterrit alors
; dans %LOCALAPPDATA%\Programs\Ex-libris plutôt que dans « Program Files ».
;
; C'est le bon choix ici, et pas seulement par confort. Ex-libris n'écrit que
; dans les données de l'utilisateur ; elle n'a aucun besoin d'un dossier
; commun à toute la machine. Et sur l'ordinateur de quelqu'un d'autre, une
; demande d'élévation transforme « installe ce petit programme » en « donne
; les pleins pouvoirs à un exécutable venu par clé USB ».
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#Nom}
DefaultGroupName={#Nom}
DisableProgramGroupPage=yes

; Un seul fichier, posé où on le lance.
OutputDir=.
OutputBaseFilename=Installer-Exlibris-{#Version}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; L'icône de l'installateur ET celle de l'entrée « Applications installées ».
; Sans elle, Windows affiche l'icône générique d'un programme d'installation,
; et l'utilisateur ne reconnaît pas ce qu'il vient de recevoir.
SetupIconFile=..\Exlibris\Assets\exlibris.ico
UninstallDisplayIcon={app}\{#Executable}

; L'exécutable publié est en 64 bits : le déclarer évite que Windows ne
; redirige les chemins vers les dossiers 32 bits.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "bureau"; Description: "Créer un raccourci sur le Bureau"; \
    GroupDescription: "Raccourcis :"

[Files]
Source: "{#Source}\{#Executable}"; DestDir: "{app}"; Flags: ignoreversion

; La structure des réglages, sans valeur : elle dit quoi remplir.
Source: "{#Source}\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion

; ----- Les réglages LOCAUX : clé Syncfusion, adresse de l'API Lirevia.
;
; ⚠️ « onlyifdoesntexist » est indispensable. Ce fichier diffère d'un poste à
; l'autre — celui-ci connaît le serveur Lirevia, celui-là non — et une mise à
; jour qui l'écraserait effacerait un réglage que personne n'a noté ailleurs.
; Il n'est donc posé qu'à la PREMIÈRE installation.
;
; ⚠️ Il porte aussi la clé de licence Syncfusion. Pour un cercle familial, cela
; ne pose pas de question. Avant toute diffusion plus large, vérifiez ce que
; votre licence autorise : distribuer votre clé à des inconnus n'est pas la
; même chose que la poser sur trois ordinateurs qui sont les vôtres.
; Retirez cette ligne pour livrer sans clé — l'application démarrera avec la
; bannière d'évaluation de Syncfusion.
Source: "{#Source}\appsettings.Local.json"; DestDir: "{app}"; \
    Flags: onlyifdoesntexist

[Icons]
Name: "{group}\{#Nom}"; Filename: "{app}\{#Executable}"
Name: "{group}\Désinstaller {#Nom}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#Nom}"; Filename: "{app}\{#Executable}"; Tasks: bureau

[Run]
Filename: "{app}\{#Executable}"; \
    Description: "Lancer {#Nom}"; \
    Flags: nowait postinstall skipifsilent

; =====================================================================
;  ⚠️ AUCUNE SECTION [UninstallDelete], ET C'EST DÉLIBÉRÉ.
;
;  La bibliothèque vit dans %LOCALAPPDATA%\Exlibris — le fichier exlibris.db
;  et le dossier couvertures\. Inno ne supprime que ce qu'il a installé, donc
;  ces données survivent naturellement à une désinstallation.
;
;  Ne cédez pas à la tentation d'y ajouter un nettoyage « propre » : une
;  désinstallation faite pour réinstaller proprement effacerait deux mille
;  fiches et sept méga-octets de couvertures, sans confirmation et sans
;  retour. Un utilisateur qui veut vraiment tout effacer sait supprimer un
;  dossier.
; =====================================================================
