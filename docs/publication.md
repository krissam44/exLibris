# Ex-libris — publier une version

> **Une commande suffit.** Le reste de ce document explique ce qui se passe
> derrière, pour le jour où elle s'arrêtera au milieu.
>
> ```powershell
> cd "D:\WorkPlace\Projet Exlibris"
> powershell -ExecutionPolicy Bypass -File .\publier-photino.ps1
> ```
>
> `-Release` publie en plus la Release GitHub (installateur et archive Linux
> joints), **sans** `appsettings.Local.json`. `-SansSetup` s'arrête aux
> publications.

Depuis le 12/11/2026, Ex-libris n'existe plus qu'en **Photino.Blazor**
(`Exlibris.Photino`), pour Windows et Linux. La version WPF, son script
`publier.ps1` et son installateur `Exlibris.iss` ont été retirés du dépôt ;
l'historique Git les conserve.

---

## 1. La chaîne

| | Objet | Poids (04/10/2026) |
|---|---|---|
| Source | `Exlibris.slnx` | Photino, Data, Shared, Scanner, Tests |
| Publication Windows | `publish\photino\win-x64\` (dossier) | 114 Mo |
| Installateur Windows | `installateur\Installer-Exlibris-Photino-<version>.exe` | 33 Mo |
| Publication Linux | `publish\photino\Exlibris-linux-x64.tar.gz` | 43 Mo |
| Poste cible Windows | `%LOCALAPPDATA%\Programs\Ex-libris Photino` | sans UAC |

---

## 2. Les étapes

### 2.1 Les tests, avant tout le reste

`publier-photino.ps1` lance `dotnet test` et s'arrête si quoi que ce soit
échoue. Une fois l'installateur posé sur trois ordinateurs, corriger demande
de refaire la tournée : quelques secondes ici valent un déplacement en moins.

### 2.2 La publication

`dotnet publish` pour `win-x64` puis `linux-x64`, `--self-contained` (le poste
n'a besoin d'**aucun .NET installé**) et `DebugType=none` (pas de `.pdb`).

- **Un dossier, pas un fichier unique** : Photino lit `wwwroot` (pages,
  styles, scripts) sur le disque. En fichier unique, la fenêtre resterait
  blanche.
- **Les thèmes Syncfusion sont élagués** à la publication (cible
  `ElaguerThemesSyncfusion` du projet) : un seul est chargé, `bootstrap5.css`,
  sur plus de 200 Mo livrés par le paquet.

> ⚠️ **Ne jamais ajouter `PublishTrimmed`.** Le rognage supprime ce qu'il croit
> inutilisé, or Syncfusion, MudBlazor et Dapper trouvent l'essentiel de leurs
> types par réflexion. L'application se compile, se publie, et échoue à
> l'ouverture d'un écran — chez quelqu'un d'autre, avec un message
> incompréhensible.

### 2.3 L'installateur Windows

Inno Setup lit `installateur\ExlibrisPhotino.iss`. Les décisions qui comptent,
chacune commentée dans le fichier :

| Décision | Pourquoi |
|---|---|
| `AppId` fixe | C'est lui, et non le nom, qui permet à Windows de **remplacer** une version installée au lieu d'en poser une seconde à côté. **Ne le changez jamais.** |
| `PrivilegesRequired=lowest` | Aucune fenêtre UAC : sur l'ordinateur de quelqu'un d'autre, une demande d'élévation transforme « installe ce petit programme » en « donne les pleins pouvoirs à un exécutable venu par clé USB ». |
| `onlyifdoesntexist` | Sur `appsettings.Local.json`, qui diffère d'un poste à l'autre. Une mise à jour qui l'écraserait effacerait un réglage que personne n'a noté ailleurs. |
| Version lue dans l'exe | Elle vient de `<Version>` du csproj. Recopiée dans le script Inno, elle finirait par diverger. |
| Pas de `[UninstallDelete]` | Voir §2.4 : la bibliothèque ne doit pas partir avec le programme. |

### 2.4 Ce qui arrive sur le poste

| Dossier | Contenu | À la désinstallation |
|---|---|---|
| `%LOCALAPPDATA%\Programs\Ex-libris Photino` | l'application, les deux fichiers de réglages, le désinstalleur | supprimé |
| `%LOCALAPPDATA%\Exlibris` | `exlibris.db`, le dossier `couvertures\`, `erreurs.log` | **conservé** |

> **Sauvegarder, c'est copier le second dossier.** La base seule rendrait une
> bibliothèque sans aucune image, et l'application ne le signalerait pas.
>
> ```powershell
> robocopy "%LOCALAPPDATA%\Exlibris" "E:\Sauvegarde\Exlibris" /MIR
> ```

Un poste qui porte encore l'**ancien Ex-libris WPF** le retire par
« Désinstaller Ex-libris » : sa bibliothèque est la même, elle n'est pas
touchée.

---

## 3. Liste de contrôle, à chaque version

1. Incrémenter `<Version>` dans `Exlibris.Photino\Exlibris.Photino.csproj` —
   **seul endroit à modifier**. Le pied de page de l'application et le nom de
   l'installateur en découlent.
2. Fermer Ex-libris et Visual Studio, sinon la compilation butera sur des DLL
   verrouillées.
3. Lancer `publier-photino.ps1` (avec `-Release` pour une diffusion publique).
4. Installer sur son propre poste et vérifier que les ouvrages sont toujours là.

Si le logo change, `installateur\faire-icone.ps1` régénère
`Exlibris.Photino\Assets\exlibris.ico` à ses sept tailles (16 à 256 px). Elle
est versionnée : la compilation ne la refabrique pas, et c'est voulu — un
binaire ne doit pas dépendre d'un outil graphique.

---

## 4. Trois choses qui surprennent

### Windows affiche un avertissement

L'installateur n'est pas signé numériquement. SmartScreen montre « L'ordinateur
a été protégé par Windows », avec *Informations complémentaires* puis *Exécuter
quand même*. En famille, un mot d'explication suffit.

### Inno Setup s'installe à plusieurs endroits possibles

Selon qu'il obtient ou non l'élévation, winget le pose dans
`%LOCALAPPDATA%\Programs` ou dans `Program Files (x86)`. Le script cherche aux
trois emplacements possibles.

### La clé Syncfusion voyage dans l'installateur… sauf en `-Release`

`appsettings.Local.json` (dans `Exlibris.Photino\`, jamais versionné) la
contient. Une publication ordinaire l'emporte : pour vos propres postes, c'est
voulu. Avec `-Release`, la propriété `SansReglagesLocaux` l'écarte de la
compilation, et le script vérifie encore son absence avant l'envoi.

---

## 5. Linux

Prérequis : glibc 2.34 ou plus (Ubuntu 22.04+, Debian 12+, Zorin OS 17+), et
les paquets `libwebkit2gtk-4.1-0` et `libnotify4`. Sur le poste, extraire
l'archive dans un dossier, puis :

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

---

## 6. Les fichiers

| Fichier | Rôle |
|---|---|
| `publier-photino.ps1` | tests → publications Windows et Linux → installateur → Release (avec `-Release`) |
| `installateur\ExlibrisPhotino.iss` | le script Inno Setup |
| `installateur\faire-icone.ps1` | régénère `Exlibris.Photino\Assets\exlibris.ico` depuis un PNG |
| `publier-scanner.ps1` | l'APK du scanner Android (voir `Exlibris.Scanner\signature.example.props`) |
