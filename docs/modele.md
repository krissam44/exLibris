# Ex-libris — le modèle de données

> **Ce document est à relire et à corriger avant d'écrire une ligne de code.**
> Tout le reste en découle : les écrans, l'import, la recherche.
>
> Chiffres de référence : ~2 000 ouvrages pour une bibliothèque adulte,
> ~5 000 bandes dessinées pour une autre, **dont 30 % sans ISBN**. Trois
> installations indépendantes, une base SQLite chacune, un seul utilisateur.

---

## 1. Ce qui change par rapport à Lirevia, et pourquoi

Trois contraintes de Lirevia disparaissent, et chacune supprime une table.

| Contrainte Lirevia | Conséquence là-bas | Ici |
|---|---|---|
| Catalogue **partagé** entre bibliothèques | `livres` sans `association_id`, et un `classements_ouvrages` pour que chacune ait son rayon | Le classement redevient **une colonne** de l'ouvrage |
| **Prêt** d'exemplaires physiques | Table `exemplaires`, statut, code-barres | **Supprimée** — voir §3.1 |
| **Multi-tenant** | `association_id` sur toute table métier | Rien : une base, un propriétaire |

Une contrainte apparaît, et c'est elle qui justifie l'application :

> **La série et le tome.** Lirevia prête des titres ; vous collectionnez des
> séries. Savoir qu'il manque le tome 12 est la question centrale d'un fonds de
> 5 000 albums, et Lirevia ne sait pas la poser.

Et un chiffre commande tout le reste : **30 % sans ISBN**, soit environ 1 500
albums. La saisie manuelle n'est pas un repli, c'est un chemin principal. Le
modèle doit la rendre RAPIDE — d'où la table des auteurs et celle des séries,
qui existent d'abord pour l'autocomplétion.

---

## 2. Le schéma

```sql
-- =====================================================================
--  Ex-libris — bibliothèque personnelle (SQLite)
--
--  ⚠️ RÈGLE TRANSVERSALE : chaque table de référentiel porte une colonne
--  `cle_recherche`, CALCULÉE EN C#, jamais en SQL. Voir §3.5 — c'est la
--  décision la plus structurante du modèle.
-- =====================================================================

PRAGMA foreign_keys = ON;   -- ⚠️ à poser sur CHAQUE connexion : SQLite
                            -- l'a désactivé par défaut, et une clé
                            -- étrangère ignorée ne lève rien du tout.

-- ---------------------------------------------------------------------
-- 1. Référentiels
-- ---------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS editeurs (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    nom            TEXT NOT NULL,
    -- Forme repliée (casse, accents, ponctuation, préfixe « Éditions »),
    -- produite par CleReferentiel.Editeur. C'est ELLE qui porte l'unicité.
    cle_recherche  TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS auteurs (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    -- « Rosiński, Grzegorz » — forme d'affichage, telle qu'on veut la lire.
    nom            TEXT NOT NULL,
    cle_recherche  TEXT NOT NULL UNIQUE
);

-- Le rayon, tel que VOUS le concevez. Un seul par ouvrage (voir §3.4).
CREATE TABLE IF NOT EXISTS classements (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    nom            TEXT NOT NULL,
    cle_recherche  TEXT NOT NULL UNIQUE,
    -- Ordre d'affichage choisi à la main : l'alphabet n'est pas l'ordre
    -- de vos étagères.
    rang           INTEGER NOT NULL DEFAULT 0
);

-- ---------------------------------------------------------------------
-- 2. Séries — le cœur de l'application
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS series (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    nom            TEXT NOT NULL,
    cle_recherche  TEXT NOT NULL UNIQUE,

    -- Nombre de tomes que la série COMPTE, quand on le sait. NULL sinon.
    --
    -- Sans lui, on détecte les trous INTÉRIEURS (on a 1,2,3,5 → il manque
    -- le 4) mais jamais la queue (on a 1 à 20, la série en compte 24). Les
    -- deux manques n'ont pas la même valeur pour un collectionneur : le
    -- second est une liste de courses.
    tomes_annonces INTEGER NULL,

    -- Série terminée : ce qui distingue « il me manque la fin » de « la
    -- suite n'est pas encore parue ». Renseigné à la main.
    terminee       INTEGER NOT NULL DEFAULT 0,

    note           TEXT NOT NULL DEFAULT ''
);

-- ---------------------------------------------------------------------
-- 3. Les ouvrages
--
--    UNE LIGNE = UN LIVRE POSÉ SUR VOTRE ÉTAGÈRE. Pas d'œuvre abstraite,
--    pas d'exemplaire séparé — voir §3.1.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS livres (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,

    -- NULL pour les ~30 % d'albums sans code-barres.
    --
    -- ⚠️ PAS D'UNIQUE, et c'est une décision : on peut posséder deux fois
    -- le même album (deux cadeaux identiques). Le doublon est SIGNALÉ à la
    -- saisie, jamais interdit — c'est votre bibliothèque, pas un
    -- référentiel partagé. Voir §3.1.
    --
    -- Écrire NULL et jamais une chaîne vide : sans cela, tous les albums
    -- sans code-barres se ressembleraient au moment de chercher un doublon.
    isbn           TEXT NULL,

    titre          TEXT NOT NULL,
    cle_recherche  TEXT NOT NULL,          -- titre replié, pour la recherche

    serie_id       INTEGER NULL REFERENCES series(id) ON DELETE SET NULL,

    -- Entier pour pouvoir TRIER et détecter les trous. Les hors-séries,
    -- intégrales et « tome 0 » restent à NULL : ils ne participent pas à
    -- la numérotation, et leur mention vit dans le titre.
    numero_tome    INTEGER NULL,

    editeur_id     INTEGER NULL REFERENCES editeurs(id) ON DELETE SET NULL,
    classement_id  INTEGER NULL REFERENCES classements(id) ON DELETE SET NULL,

    -- Emplacement PHYSIQUE, texte libre : « Salle 1, étagère B ».
    -- Renseigné en masse par l'import du scan (voir §4).
    rangement      TEXT NOT NULL DEFAULT '',

    annee          INTEGER NULL,
    nb_pages       INTEGER NULL,
    format         TEXT NOT NULL DEFAULT '',   -- Broché, Relié, Intégrale…
    resume         TEXT NOT NULL DEFAULT '',
    couverture     TEXT NOT NULL DEFAULT '',   -- chemin relatif, voir §3.6

    -- ----- Ce qui n'appartient qu'à une bibliothèque personnelle
    lu             INTEGER NOT NULL DEFAULT 0,
    date_lecture   TEXT NULL,                  -- ISO-8601, voir §3.7
    appreciation   INTEGER NULL,               -- 1 à 5, NULL = pas d'avis
    commentaire    TEXT NOT NULL DEFAULT '',

    -- ----- Traçabilité de l'enrichissement
    -- D'où vient la notice : « BnF », « Google Books », « Open Library »,
    -- ou vide si saisie à la main. Dit ce qu'on peut croire.
    source_notice  TEXT NOT NULL DEFAULT '',

    -- Date de la dernière TENTATIVE d'enrichissement, réussie ou non.
    -- Reprise directe de Lirevia, et indispensable ici : les quotas
    -- gratuits se comptent en centaines d'appels par jour, l'enrichissement
    -- de 7 000 fiches s'étale donc sur une semaine. La vider relance.
    enrichi_le     TEXT NULL,

    cree_le        TEXT NOT NULL DEFAULT (datetime('now')),
    modifie_le     TEXT NULL
);

-- Index NON unique : sert la recherche par code-barres et le contrôle de
-- doublon à la saisie, sans interdire le doublon lui-même.
CREATE INDEX IF NOT EXISTS idx_livres_isbn       ON livres(isbn);
CREATE INDEX IF NOT EXISTS idx_livres_serie      ON livres(serie_id, numero_tome);
CREATE INDEX IF NOT EXISTS idx_livres_classement ON livres(classement_id);
CREATE INDEX IF NOT EXISTS idx_livres_recherche  ON livres(cle_recherche);
CREATE INDEX IF NOT EXISTS idx_livres_enrichi    ON livres(enrichi_le);

-- ---------------------------------------------------------------------
-- 4. Auteurs d'un ouvrage, AVEC LEUR RÔLE
--
--    Indispensable pour la BD : Van Hamme écrit, Rosiński dessine, et on
--    cherche parfois l'un, parfois l'autre. Un champ texte unique
--    « Van Hamme, Rosiński » ne permet ni de chercher ni de distinguer.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS livre_auteurs (
    livre_id   INTEGER NOT NULL REFERENCES livres(id)  ON DELETE CASCADE,
    auteur_id  INTEGER NOT NULL REFERENCES auteurs(id) ON DELETE CASCADE,
    -- 'scenario' | 'dessin' | 'couleur' | 'auteur' (indéterminé) | 'traduction'
    role       TEXT NOT NULL DEFAULT 'auteur',
    -- Ordre d'affichage : le premier cité reste le premier cité.
    rang       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (livre_id, auteur_id, role)
);

CREATE INDEX IF NOT EXISTS idx_livre_auteurs_auteur ON livre_auteurs(auteur_id);
```

---

## 3. Les décisions, et ce qu'elles coûtent

### 3.1 Pas de table `exemplaires`

Dans Lirevia, la séparation œuvre/exemplaire existe pour deux raisons : la fiche
est **partagée** entre bibliothèques, et c'est l'exemplaire qu'on **prête**.
Aucune des deux ne s'applique ici.

**Une ligne = un livre sur votre étagère.** Si vous possédez deux fois le même
album, ce sont deux lignes — et c'est plus juste que « un ouvrage, quantité 2 » :
vos deux exemplaires n'ont ni le même état, ni forcément le même rangement, ni
la même appréciation.

**Deux situations très différentes se ressemblent, et le modèle les traite
séparément.**

| Situation | Fréquence | Ce que fait le modèle |
|---|---|---|
| *Dune* en plusieurs collections — poche, intégrale, édition illustrée | Courante | **Rien de spécial.** Ce sont des ISBN différents, donc des lignes différentes, chacune avec son éditeur, son format, son rangement. Le modèle y répond déjà. |
| Deux exemplaires du **même** ISBN — deux cadeaux identiques | Exceptionnelle | Deux lignes portant le même ISBN. C'est ce qui a fait **retirer l'`UNIQUE`**. |

Le second cas est rare, mais le rendre impossible aurait coûté une contorsion à
la saisie le jour où il se présente. Le doublon est donc **signalé et non
refusé** : à la saisie d'un ISBN déjà présent, l'application affiche la fiche
existante et demande s'il s'agit d'un second exemplaire ou d'une erreur de scan.

C'est exactement l'inverse de Lirevia, où l'unicité de l'ISBN protège un
référentiel partagé entre bibliothèques. Ici il n'y a rien à protéger : c'est
votre étagère.

### 3.2 La série et le tome

`numero_tome` est un entier pour permettre deux choses qu'aucune autre forme ne
donne : le **tri naturel** (le tome 10 après le 9, pas entre le 1 et le 2) et la
**détection des trous**.

Les trous se calculent sans rien stocker :

```sql
-- Ce qui manque dans une série, tomes intérieurs et queue.
SELECT s.nom, s.tomes_annonces, GROUP_CONCAT(l.numero_tome) AS possedes
FROM series s
LEFT JOIN livres l ON l.serie_id = s.id AND l.numero_tome IS NOT NULL
GROUP BY s.id;
```
Le calcul de la liste manquante se fait en C# sur ce résultat : à 5 000 lignes
c'est instantané, et l'exprimer en SQL rendrait la requête illisible pour rien.

### 3.3 Les auteurs avec rôle

C'est la table la plus discutable, parce qu'elle coûte : les trois sources
publiques rendent les auteurs sous des formes différentes, et il faudra les
rapprocher.

**Elle se justifie par la saisie manuelle, pas par la recherche.** Avec 1 500
albums à saisir à la main, une autocomplétion sur « Ros… » qui propose
« Rosiński, Grzegorz » fait gagner des heures. La recherche par auteur vient en
prime.

À l'import, on découpe la chaîne rendue par l'API sur les virgules et
points-virgules, et **tout arrive en rôle `auteur`**. Le rôle exact se corrige à
la main sur les séries qui comptent. Un import qui devinerait les rôles se
tromperait silencieusement.

### 3.4 Un seul classement, pas des étiquettes

Un ouvrage a **un** rayon, parce qu'il est rangé à **un** endroit. Les
étiquettes multiples (« SF », « à relire », « offert par Paul ») sont
tentantes et n'ont pas la même nature — elles décrivent, elles ne rangent pas.
Si le besoin apparaît, elles s'ajouteront en table séparée sans rien casser.

Le `rangement` reste **texte libre** avec suggestion des valeurs déjà employées,
exactement comme dans Lirevia : « Salle 1, étagère B » multiplié par deux cents
rayons ferait d'un référentiel une corvée.

### 3.5 ⚠️ La décision la plus structurante : `cle_recherche` calculée en C#

**SQLite ne replie pas les accents.** Vérifié sur SQLite 3.49 :

| Recherche | Correspondances parmi `Litterature`, `Littérature`, `LITTERATURE` |
|---|---|
| `litterature` | **2** |
| `littérature` | **1** |
| `LITTERATURE` | **2** |

La collation `NOCASE` ne traite que l'ASCII. MariaDB, avec
`utf8mb4_general_ci`, aurait replié les trois ensemble.

**C'est l'inverse exact du piège de Lirevia**, et il faut s'en souvenir dans ce
sens-là. Là-bas, la base repliait les accents et le C# ne le faisait pas
(`OrdinalIgnoreCase`) : l'index mémoire manquait l'entrée, repartait en base, y
heurtait la clé unique, et InnoDB brûlait un auto-incrément à chaque fois —
170 000 identifiants perdus, le genre suivante née avec le numéro 203.

Ici le danger est symétrique : **si on s'en remet à SQLite, « Littérature » et
« Litterature » deviennent deux rayons distincts**, et personne ne s'en aperçoit
avant d'en avoir trente.

D'où la règle : **chaque référentiel porte une `cle_recherche` produite par le
C#** (`CleReferentiel.CleCollation`, repris de Lirevia), et c'est elle qui porte
l'`UNIQUE`. Le `nom` reste la forme d'affichage.

La même colonne sur `livres` sert la recherche par titre : `LIKE '%thorgal%'`
sur `cle_recherche` trouve « Thorgal » comme « thorgal ».

### 3.6 Les couvertures : des fichiers, pas des blobs

`couverture` stocke un **chemin relatif** à un dossier `couvertures/` voisin de
la base. Trois raisons : le fichier `.db` reste petit et se sauvegarde vite, une
image se remplace sans toucher à la base, et SQLite lit mal les gros blobs.

**Sauvegarder, c'est donc copier deux choses** : le `.db` **et** le dossier. À
dire dans l'application, pas seulement dans ce document.

### 3.7 Les dates en TEXT ISO-8601

SQLite n'a pas de type date. `TEXT` au format `YYYY-MM-DD` se trie correctement
comme chaîne, se lit à l'œil, et Dapper le convertit sans cérémonie. Les entiers
Unix trient aussi mais rendent la base illisible à l'inspection.

---

## 4. Le circuit du scan

L'application MAUI produit un CSV à **deux colonnes** :

```
isbn;rangement
9782803680030;Salle 1, étagère B
9782205057805;Salle 1, étagère B
```

Le rangement se saisit **une fois par étagère** dans l'application mobile et se
recopie sur chaque ligne. C'est le seul renseignement que le physique connaît et
qu'aucune base publique ne donnera jamais.

L'import fait, dans cet ordre :

1. **normalise** l'ISBN : retire tirets et espaces, coupe le code additionnel de
   2 ou 5 chiffres que certains albums portent à droite de l'EAN, vérifie la clé
   de contrôle ;
2. **dédoublonne** le fichier — scanner deux fois la même case arrive ;
3. **crée les fiches** avec le seul ISBN et le rangement, `enrichi_le` à NULL ;
4. **enrichit** ensuite, par lots, en respectant les quotas.

L'application mobile reste **bête** : elle enregistre tout, ne valide rien, ne
dédoublonne pas. Un scanner qui refuserait un code déjà vu vous ferait manquer
les doublons réels — ces deux exemplaires que vous possédez vraiment.

Le format est déjà celui qu'attend `Lirevia.BnfImport enrichir --isbns` (un ISBN
par ligne), qui a servi sur 1 691 ISBN. La colonne rangement est en trop pour
Lirevia, qui l'ignorera.

---

## 5. Ce qui reste à trancher

> **Tranché le 29/08/2026** — l'`UNIQUE` sur l'ISBN est retiré (§3.1), et les
> colonnes de lecture (`lu`, `date_lecture`, `appreciation`, `commentaire`) sont
> conservées.

1. **Le prêt à un ami.** Vous l'avez explicitement exclu, je n'ai rien mis. Il
   s'ajouterait plus tard en deux colonnes (`prete_a`, `prete_le`) sans toucher
   au reste — je le note pour que la porte reste ouverte, pas pour la rouvrir.

2. **Les rôles d'auteur.** La liste proposée — scénario, dessin, couleur,
   auteur, traduction — vous convient-elle ? Faut-il « lettrage », « préface » ?

3. **`tomes_annonces` et `terminee`** se saisissent à la main : aucune API ne les
   donne de façon fiable. Acceptez-vous ce travail sur les séries qui comptent,
   ou préférez-vous se contenter des trous intérieurs ?

---

## 6. Ce que je n'ai délibérément pas fait

- **Pas de FTS5.** À 7 000 lignes, un `LIKE '%…%'` sur `cle_recherche` répond en
  quelques millisecondes. L'index plein texte de Lirevia existait pour 540 000
  fiches ; le transposer ici serait de la complexité sans contrepartie.
- **Pas de pagination par curseur.** Même raison : la grille Syncfusion WPF
  charge 7 000 lignes sans effort.
- **Pas de table `genres` distincte du classement.** Elles n'étaient deux
  dans Lirevia qu'à cause du catalogue partagé.
