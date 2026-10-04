# Ex-libris — publier une version

> **Une commande suffit.** Le reste de ce document explique ce qui se passe
> derrière, pour le jour où elle s'arrêtera au milieu.
>
> ```powershell
> cd "D:\WorkPlace\Projet Exlibris"
> powershell -ExecutionPolicy Bypass -File .\publier.ps1
> ```
>
> Mesures relevées le 30/08/2026 sur Windows 11, .NET 10, Inno Setup 6.7.3.

---

## 1. La chaîne

Quatre objets, chacun fabriqué à partir du précédent.

| | Objet | Poids |
|---|---|---|
| Source | `Exlibris.slnx` | 4 projets |
| Publication | `publish\compresse\Exlibris.exe` | **65,5 Mo** |
| Installateur | `installateur\Installer-Exlibris-0.1.0.0.exe` | **60,7 Mo** |
| Poste cible | `%LOCALAPPDATA%\Programs\Ex-libris` | sans UAC |

L'installateur est *plus léger* que l'exécutable qu'il contient : Inno le
recompresse en LZMA2, plus efficace que la compression intégrée au fichier
unique.

---

## 2. Les quatre étapes

### 2.1 Les tests, avant tout le reste

`publier.ps1` lance `dotnet test` et s'arrête si quoi que ce soit échoue.

Ce n'est pas de la coquetterie. Une fois l'installateur posé sur trois
ordinateurs, corriger demande de refaire la tournée : quelques secondes ici
valent un déplacement en moins.

### 2.2 La publication

Un seul `dotnet publish`, avec cinq réglages qui comptent chacun pour une
raison précise.

| Réglage | Ce qu'il change |
|---|---|
| `--self-contained` | Le poste cible n'a besoin d'**aucun .NET installé**. C'est ce qui rend l'application donnable sans mode d'emploi — et ce qui explique les 65 Mo, qui sont surtout le runtime. |
| `PublishSingleFile` | Un seul `.exe`, sans dossier de DLL à côté. |
| `IncludeNativeLibrariesForSelfExtract` | Sans lui, `e_sqlite3.dll` resterait **dehors** du fichier unique. L'application démarrerait et planterait à la première requête, sur un poste où cette bibliothèque est absente. |
| `EnableCompressionInSingleFile` | 149 Mo → 65,5 Mo. Le prix est un démarrage un peu plus lent : le fichier se décompresse au lancement. |
| `DebugType=none` | Sans lui, les `.pdb` partent avec — inutiles chez l'utilisateur. |

> ⚠️ **Ne jamais ajouter `PublishTrimmed`.** Le rognage supprime ce qu'il croit
> inutilisé, or Syncfusion, Dapper et WPF trouvent l'essentiel de leurs types
> par réflexion. L'application se compile, se publie, et échoue à l'ouverture
> d'un écran — chez quelqu'un d'autre, avec un message incompréhensible. C'est
> le genre d'optimisation qu'on regrette trois semaines plus tard.

### 2.3 L'installateur

Inno Setup lit `installateur\Exlibris.iss`. Cinq décisions y sont inscrites,
chacune commentée dans le fichier.

| Décision | Pourquoi |
|---|---|
| `AppId` fixe | C'est lui, et non le nom, qui permet à Windows de **remplacer** une version installée au lieu d'en poser une seconde à côté. **Ne le changez jamais.** |
| `PrivilegesRequired=lowest` | Aucune fenêtre UAC. Sur l'ordinateur de quelqu'un d'autre, une demande d'élévation transforme « installe ce petit programme » en « donne les pleins pouvoirs à un exécutable venu par clé USB ». |
| `onlyifdoesntexist` | Sur `appsettings.Local.json`. Ce fichier diffère d'un poste à l'autre — celui-ci connaît le serveur Lirevia, celui-là non. Une mise à jour qui l'écraserait effacerait un réglage que personne n'a noté ailleurs. |
| Version lue dans l'exe | Elle vient de `<Version>` du csproj. Recopiée dans le script Inno, elle finirait par diverger — et c'est l'installateur qui aurait tort, lui qui compare les numéros. |
| Pas de `[UninstallDelete]` | Voir §2.4 : la bibliothèque ne doit pas partir avec le programme. |

### 2.4 Ce qui arrive sur le poste

**Deux dossiers, et il faut les distinguer.**

| Dossier | Contenu | À la désinstallation |
|---|---|---|
| `%LOCALAPPDATA%\Programs\Ex-libris` | l'exécutable, les deux fichiers de réglages, le désinstalleur | supprimé |
| `%LOCALAPPDATA%\Exlibris` | `exlibris.db` et le dossier `couvertures\` | **conservé** |

Le script Inno ne contient délibérément aucune section `[UninstallDelete]` :
une désinstallation faite pour réinstaller proprement effacerait deux mille
fiches et le dossier de couvertures, sans confirmation et sans retour.

> **Sauvegarder, c'est copier le second dossier.** La base seule rendrait une
> bibliothèque sans aucune image, et l'application ne le signalerait pas — elle
> afficherait « Pas de couverture » partout, ce qui ressemble à une perte de
> données sans en être une.
>
> ```powershell
> robocopy "%LOCALAPPDATA%\Exlibris" "E:\Sauvegarde\Exlibris" /MIR
> ```

---

## 3. Liste de contrôle, à chaque version

1. Incrémenter `<Version>` dans `Exlibris\Exlibris.csproj` — **seul endroit à
   modifier**. Le pied de page de l'application et le nom de l'installateur en
   découlent.
2. Fermer Ex-libris et Visual Studio, sinon la compilation butera sur des DLL
   verrouillées.
3. Lancer `publier.ps1`.
4. Installer sur son propre poste et vérifier que les ouvrages sont toujours là.
5. Distribuer `installateur\Installer-Exlibris-<version>.exe`.

Si le logo change, `installateur\faire-icone.ps1` régénère l'icône à ses sept
tailles (16 à 256 px). Elle est versionnée avec le projet : la compilation ne la
refabrique pas, et c'est voulu — un binaire ne doit pas dépendre d'un outil
graphique.

---

## 4. Trois choses qui surprennent

### Windows affiche un avertissement

L'installateur n'est pas signé numériquement. SmartScreen montre « L'ordinateur
a été protégé par Windows », avec *Informations complémentaires* puis *Exécuter
quand même*.

En famille, un mot d'explication suffit. Pour une diffusion plus large, il
faudrait un certificat de signature de code — payant, et sans effet immédiat :
SmartScreen accorde sa confiance à l'usage, pas au certificat seul.

### Inno Setup s'installe à deux endroits possibles

Selon qu'il obtient ou non l'élévation, winget le pose dans
`%LOCALAPPDATA%\Programs` ou dans `Program Files (x86)`. Sur ce poste, c'est le
premier.

`publier.ps1` cherche aux trois emplacements possibles — un défaut qu'il a fallu
corriger le jour de la première installation, le script ne regardant alors que
dans `Program Files (x86)` et déclarant l'outil absent alors qu'il était là.

### La clé Syncfusion voyage dans l'installateur

`appsettings.Local.json` la contient et part avec.

Pour trois ordinateurs qui sont les vôtres, la question ne se pose pas.
**Avant toute diffusion plus large**, vérifier ce que la licence autorise : la
poser chez soi n'est pas la même chose que la distribuer à des inconnus. La
ligne à retirer est signalée dans le script Inno — sans elle, l'application
démarre avec la bannière d'évaluation de Syncfusion.

---

## 5. Les fichiers

| Fichier | Rôle |
|---|---|
| `publier.ps1` | tests → publication → installateur |
| `installateur\Exlibris.iss` | le script Inno Setup |
| `installateur\faire-icone.ps1` | régénère `Exlibris\Assets\exlibris.ico` depuis un PNG |
| `lirevia-logo-front-bleu.png` | le visuel source de l'icône — **ne pas déplacer**, c'est le chemin par défaut du script |

---

## 6. La version Photino (Windows et Linux)

Depuis le 04/10/2026, Ex-libris existe aussi en **Photino.Blazor**
(`Exlibris.Photino`), qui tourne sous Windows **et** Linux. Elle s'installe
**à côté** de la version WPF, avec son propre identifiant d'installation, et
partage la même bibliothèque. La bascule viendra quand elle aura été validée
à l'usage : ce jour-là, reprendre l'`AppId` du WPF dans
`installateur\ExlibrisPhotino.iss` pour que l'installation le remplace.

```powershell
cd "D:\WorkPlace\Projet Exlibris"
powershell -ExecutionPolicy Bypass -File .\publier-photino.ps1
```

| | Objet | Poids (04/10/2026) |
|---|---|---|
| Publication Windows | `publish\photino\win-x64\` (dossier) | 114 Mo |
| Installateur Windows | `installateur\Installer-Exlibris-Photino-*.exe` | 33 Mo |
| Publication Linux | `publish\photino\Exlibris-linux-x64.tar.gz` | 43 Mo |

**Différences avec le WPF :**

- **Un dossier, pas un fichier unique** : Photino lit `wwwroot` (pages,
  styles, scripts) sur le disque. En fichier unique, la fenêtre resterait
  blanche.
- **Les thèmes Syncfusion sont élagués** à la publication (cible
  `ElaguerThemesSyncfusion` du projet) : un seul est chargé, `bootstrap5.css`,
  sur plus de 200 Mo livrés par le paquet.
- **Windows** demande le runtime WebView2, présent d'office sous Windows 11.

**Linux** — prérequis : glibc 2.38 ou plus (Ubuntu 24.04+, Debian 13+,
Zorin OS 18+), et les paquets `libwebkit2gtk-4.1-0` et `libnotify4`.
Sur le poste, extraire l'archive dans un dossier, puis :

```bash
bash installer.sh
```

Pas besoin de `chmod` : la copie depuis Windows perd le droit d'exécution, et
le script le rétablit. L'application va dans `~/.local/opt/exlibris`, le
lanceur dans le menu ; la **bibliothèque** reste dans `~/.local/share/Exlibris`
(à sauvegarder : `exlibris.db` **et** `couvertures/`), que ni la réinstallation
ni `desinstaller.sh` ne touchent. Une réinstallation conserve le
`appsettings.Local.json` du poste. Ne jamais lancer en root.

Note : sous Linux, WebKit range aussi ses propres fichiers (`storage`,
`mediakeys`, `hsts-storage.sqlite`) dans `~/.local/share/Exlibris` ; ils ne
font pas partie de la bibliothèque.
