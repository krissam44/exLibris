<p align="center">
  <img src="logo-ExLibris.png" alt="Ex-libris" width="96" />
</p>

<h1 align="center">Ex-libris</h1>

<p align="center">
  Votre bibliothèque personnelle : livres, bandes dessinées et séries, sur votre ordinateur.<br />
  Windows et Linux · gratuit · vos données restent chez vous.
</p>

---

## Pourquoi Ex-libris

Ex-libris est né d'un besoin simple : savoir ce qu'on possède, où c'est rangé,
et **quel tome manque** à une série. Il est pensé pour des bibliothèques
familiales de quelques centaines à plusieurs milliers d'ouvrages, dont une
bonne part sans code-barres (près d'un tiers des BD anciennes n'ont pas
d'ISBN).

- **Un seul utilisateur, une base locale.** Pas de compte, pas d'abonnement,
  pas de serveur : tout tient dans un fichier SQLite et un dossier d'images.
- **Pas de prêt, pas de multi-bibliothèque** : c'est un outil de collection,
  pas un logiciel de médiathèque.

## Ce qu'il sait faire

**La bibliothèque**
- Liste triable et filtrable (titre, auteurs, éditeur, genre, catégorie,
  rangement, année, lu…), colonnes à la carte, recherche instantanée.
- **Affichage en vignettes** : les couvertures en mosaïque, avec une barre
  A…Z / 0-9 pour aller directement à une lettre.
- Une petite couverture en tête de ligne, si on le souhaite.

**La fiche d'un ouvrage**
- **Recherche par ISBN** dans quatre sources, dans cet ordre : un serveur
  Lirevia (facultatif), la **BnF**, **Google Books**, **Open Library** —
  titre, auteurs, éditeur, année, résumé et couverture remplis d'un coup.
- Saisie manuelle rapide pour les ouvrages sans ISBN, avec autocomplétion des
  auteurs, séries et éditeurs.
- Couverture récupérée en ligne, ou prise d'une image sur le disque (photo,
  scan).
- Série et numéro de tome, rangement, lu / date de lecture, appréciation,
  commentaire, propriétaire (facultatif).

**Les séries**
- Pour chaque série : tomes possédés, nombre total, **tomes manquants**,
  série terminée ou en cours. Un filtre ne montre que les séries à compléter.

**L'inventaire**
- Verser une liste d'ISBN (fichier CSV, une colonne, avec le rangement en
  option) : chaque ligne devient une fiche.
- **Compléter les fiches par lot** en ligne, avec un rythme qui ménage les
  services consultés.

**Les réglages**
- Distinguer ou non le propriétaire de chaque ouvrage.
- **Réunir les doublons** d'éditeurs, d'auteurs, de séries, de genres, de
  catégories ou de propriétaires saisis de plusieurs façons
  (« Gallimard » et « Éditions Gallimard »).

## Les trois applications du dépôt

| Projet | Plateforme | Rôle |
|---|---|---|
| `Exlibris.Photino` | Windows, Linux | **L'application principale**, en Blazor dans une fenêtre de bureau (PhotinoX.Blazor) |
| `Exlibris` | Windows | La première version, en WPF ; même base, mêmes règles |
| `Exlibris.Scanner` | Android | Scanner de codes-barres pour l'inventaire : on vise les codes-barres en rayon, il produit le fichier CSV (à envoyer par courriel, cloud ou câble) à verser dans l'inventaire |

Les trois partagent `Exlibris.Data` (accès SQLite, sources en ligne) et
`Exlibris.Shared` (modèle, règles), testés par `Exlibris.Tests`.

## Installer

Les installateurs sont publiés dans les
[Releases](../../releases) du dépôt.

**Windows 10 et 11** — lancer `Installer-Exlibris-Photino-<version>.exe`.
Aucun droit d'administrateur n'est demandé. Prérequis : le runtime WebView2,
présent d'office sous Windows 11 et installé avec Edge sous Windows 10.

**Linux** (Ubuntu 22.04+, Debian 12+, Zorin OS 17+) — extraire
`Exlibris-linux-x64.tar.gz`, ouvrir un terminal dans le dossier extrait, puis :

```bash
bash installer.sh
```

Le script vérifie les bibliothèques nécessaires et indique, s'il en manque, la
commande `sudo apt install …` à lancer. Ex-libris apparaît ensuite dans le
menu des applications.

## Vos données

| | Windows | Linux |
|---|---|---|
| Base | `%LOCALAPPDATA%\Exlibris\exlibris.db` | `~/.local/share/Exlibris/exlibris.db` |
| Couvertures | `%LOCALAPPDATA%\Exlibris\couvertures\` | `~/.local/share/Exlibris/couvertures/` |

L'installation et la désinstallation n'y touchent jamais.

> **Sauvegarder, c'est copier les deux** — la base **et** le dossier des
> couvertures, Ex-libris fermé. La base seule redonnerait une bibliothèque
> sans aucune image.

## Construire depuis les sources

**Prérequis** : [.NET 10 SDK](https://dotnet.microsoft.com/download). Sous
Windows, Visual Studio ou `dotnet` en ligne de commande. Le scanner
Android demande en plus la charge de travail MAUI.

```bash
git clone https://github.com/krissam44/exLibris.git
cd exLibris
dotnet test Exlibris.Tests/Exlibris.Tests.csproj
dotnet run --project Exlibris.Photino/Exlibris.Photino.csproj
```

**Clé Syncfusion.** La grille de la bibliothèque utilise
[Syncfusion Blazor](https://www.syncfusion.com/blazor-components), gratuit
pour les particuliers et petites structures avec la
[Community License](https://www.syncfusion.com/products/communitylicense).
Sans clé, l'application fonctionne, avec une bannière d'évaluation. La clé se
met dans `Exlibris/appsettings.Local.json` (jamais versionné) :

```json
{
  "Syncfusion": { "LicenseKey": "votre-clé" },
  "Lirevia": { "ApiUrl": "" }
}
```

`Lirevia:ApiUrl` est facultatif : vide, cette source est simplement ignorée.

**Essayer sans toucher à sa bibliothèque.** Le dossier des données se déplace
par une variable d'environnement, par exemple sur une copie :

```powershell
$env:Exlibris__DossierDonnees = "$env:TEMP\exlibris-essai"
```

**Publier** (tests, publications Windows et Linux, installateur Inno Setup) :

```powershell
.\publier-photino.ps1
```

Détails dans [docs/publication.md](docs/publication.md) ; le modèle de données
est décrit dans [docs/modele.md](docs/modele.md).

## Technique

.NET 10 · PhotinoX.Blazor · MudBlazor · Syncfusion Blazor Grid · SQLite +
Dapper · xUnit (tests sur une vraie base SQLite jetable, sans simulacre).

## Licence

Ex-libris est distribué sous [licence MIT](LICENSE) : libre à chacun de
l'utiliser, le modifier et le redistribuer, en conservant la mention de
copyright.

Les composants tiers gardent leur propre licence. Syncfusion Blazor, en
particulier, n'est pas libre : son usage gratuit relève de la
[Community License](https://www.syncfusion.com/products/communitylicense),
et chacun doit obtenir sa propre clé.
