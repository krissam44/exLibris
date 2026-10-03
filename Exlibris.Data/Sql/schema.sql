-- =====================================================================
--  Ex-libris — bibliothèque personnelle (SQLite)
--
--  Une base par installation, un seul utilisateur, aucun prêt.
--  Le raisonnement complet est dans docs/modele.md ; ce fichier n'en
--  porte que ce qu'il faut savoir en le lisant.
--
--  ⚠️ RÈGLE TRANSVERSALE : chaque référentiel porte une `cle_recherche`
--  CALCULÉE EN C# (CleReferentiel), jamais par SQLite. La collation
--  NOCASE ne replie que l'ASCII — vérifié sur SQLite 3.49 :
--  « Litterature » et « LITTERATURE » se rejoignent, « Littérature »
--  reste séparée. S'en remettre à la base ferait deux rayons distincts
--  sans que personne s'en aperçoive avant d'en avoir trente.
--
--  C'est l'INVERSE du piège de Lirevia, où MariaDB repliait les accents
--  et où le C# ne le faisait pas — 170 000 identifiants brûlés.
-- =====================================================================

PRAGMA foreign_keys = ON;   -- ⚠️ à poser sur CHAQUE connexion : SQLite le
                            -- désactive par défaut, et une clé étrangère
                            -- ignorée ne lève strictement rien.

-- ---------------------------------------------------------------------
-- 1. Référentiels
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS editeurs (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    nom            TEXT NOT NULL,
    cle_recherche  TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS auteurs (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    nom            TEXT NOT NULL,          -- « Rosiński, Grzegorz »
    cle_recherche  TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS classements (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    nom            TEXT NOT NULL,
    cle_recherche  TEXT NOT NULL UNIQUE,
    -- Ordre d'affichage choisi à la main : l'alphabet n'est pas l'ordre
    -- de vos étagères.
    rang           INTEGER NOT NULL DEFAULT 0
);

-- Le genre au sens de l'édition : « Roman », « Bande dessinée », « Thriller ».
--
-- ⚠️ DEUX tables, et non une seule à deux usages. Le genre décrit ce
-- QU'EST le livre : elle vient de l'éditeur ou d'une notice en ligne, elle est
-- la même chez tout le monde, et l'enrichissement automatique la renseignera
-- sans rien demander. Le classement décrit où VOUS le rangez : « À lire »,
-- « Les BD de Paul », « Prêté à Marie » — personne d'autre ne l'écrirait
-- pareil, et aucune API ne le devinera.
--
-- Les confondre coûterait cher dans les deux sens : un enrichissement par lot
-- écraserait vos rayons, et un rangement personnel polluerait le genre au point
-- de rendre « tous mes thrillers » infaisable. La liste des genres se sème une
-- fois (voir ReferentielDepot.SemerGenresSiVide) et bouge peu ; celle des
-- classements naît de votre saisie et n'appartient qu'à vous.
CREATE TABLE IF NOT EXISTS genres (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    nom            TEXT NOT NULL,
    cle_recherche  TEXT NOT NULL UNIQUE
);

-- À qui appartient l'ouvrage — « moi », « ma femme », « les enfants ».
--
-- ⚠️ FACULTATIF, et la table existe quand même. Beaucoup de bibliothèques
-- n'ont qu'un propriétaire, et leur imposer ce champ serait leur faire remplir
-- une case dont la réponse est toujours la même. Le paramètre
-- « proprietaires.actifs » commande son affichage ; la table, elle, ne coûte
-- rien quand elle est vide, alors qu'une colonne ajoutée plus tard imposerait
-- une migration à tout le monde.
CREATE TABLE IF NOT EXISTS proprietaires (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    nom            TEXT NOT NULL,
    cle_recherche  TEXT NOT NULL UNIQUE
);

-- Les libellés qu'on a décidé de rapprocher à la main.
--
-- ⚠️ SANS CETTE TABLE, UNE FUSION NE TIENT PAS. Le même éditeur arrive sous
-- quatre noms selon la source — « L.G.F - Le livre de poche » (BnF),
-- « le Livre de poche », « LGF », et jusqu'à « French and European
-- Publications Inc, LGF » que rend Google Books. Aucune clé calculée ne peut
-- les rapprocher : celle qui y parviendrait fusionnerait aussi « Gallimard » et
-- « Gallimard Jeunesse ». C'est donc un humain qui tranche.
--
-- Mais une fusion sans mémoire se défait : la notice suivante recréerait le
-- libellé absorbé, et le ménage serait à refaire tous les mois. La clé absorbée
-- est donc conservée ici, et la résolution la consulte AVANT de créer.
--
-- `referentiel` porte le nom de la table visée : la table est délibérément
-- polymorphe, comme ResoudreOuCreer qui la sert. Pas de clé étrangère, donc :
-- une cible supprimée laisse un alias orphelin, que la résolution ignore et que
-- la fusion suivante écrase.
CREATE TABLE IF NOT EXISTS alias_referentiel (
    referentiel    TEXT NOT NULL,          -- 'editeurs', 'genres', ...
    cle_recherche  TEXT NOT NULL,          -- la clé du libellé absorbé
    cible_id       INTEGER NOT NULL,       -- vers qui elle pointe désormais
    PRIMARY KEY (referentiel, cle_recherche)
);

-- Les réglages de l'application : une clé, une valeur, rien de plus.
--
-- Un simple couple plutôt qu'une colonne par réglage : chaque nouveau réglage
-- demanderait sinon sa migration, sur toutes les bases déjà installées. Les
-- valeurs sont du texte, la lecture les interprète — voir ParametreDepot.
CREATE TABLE IF NOT EXISTS parametres (
    cle    TEXT PRIMARY KEY,
    valeur TEXT NOT NULL DEFAULT ''
);

-- ---------------------------------------------------------------------
-- 2. Séries — ce qui n'existe pas dans Lirevia, et qui justifie l'appli
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS series (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    nom            TEXT NOT NULL,
    cle_recherche  TEXT NOT NULL UNIQUE,
    -- Nombre de tomes que la série COMPTE, quand on le sait. Sans lui on
    -- détecte les trous intérieurs (1,2,3,5 → il manque le 4) mais jamais
    -- la queue (1 à 20 sur 24) — or c'est celle-là qui est une liste de
    -- courses. Aucune API ne le donne de façon fiable : saisi à la main.
    tomes_annonces INTEGER NULL,
    -- Distingue « il me manque la fin » de « la suite n'est pas parue ».
    terminee       INTEGER NOT NULL DEFAULT 0,
    note           TEXT NOT NULL DEFAULT ''
);

-- ---------------------------------------------------------------------
-- 3. Les ouvrages
--
--    UNE LIGNE = UN LIVRE POSÉ SUR L'ÉTAGÈRE. Pas d'œuvre abstraite, pas
--    d'exemplaire séparé : la séparation de Lirevia existait parce que la
--    fiche y est partagée entre bibliothèques et que l'exemplaire s'y
--    prête. Ni l'un ni l'autre ici.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS livres (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,

    -- NULL pour les ~30 % d'albums sans code-barres.
    --
    -- ⚠️ PAS D'UNIQUE, et c'est une décision : on peut posséder deux fois
    -- le même album (deux cadeaux identiques). Le doublon est SIGNALÉ à la
    -- saisie, jamais interdit. Le même titre en plusieurs collections —
    -- Dune en poche et en intégrale — porte de toute façon des ISBN
    -- différents et n'a jamais posé de problème.
    --
    -- Écrire NULL, jamais une chaîne vide : sinon tous les albums sans
    -- code-barres se ressembleraient au contrôle de doublon.
    isbn           TEXT NULL,

    titre          TEXT NOT NULL,
    cle_recherche  TEXT NOT NULL,          -- titre replié, pour la recherche

    serie_id       INTEGER NULL REFERENCES series(id) ON DELETE SET NULL,
    -- Entier, pour TRIER (le tome 10 après le 9) et détecter les trous.
    -- Hors-séries, intégrales et « tome 0 » restent à NULL : ils ne
    -- participent pas à la numérotation, leur mention vit dans le titre.
    numero_tome    INTEGER NULL,

    editeur_id     INTEGER NULL REFERENCES editeurs(id) ON DELETE SET NULL,

    -- Le genre (ce que le livre EST) et le rayon (où VOUS le rangez) : deux
    -- colonnes parce que ce sont deux questions. Voir la table `genres`.
    genre_id   INTEGER NULL REFERENCES genres(id) ON DELETE SET NULL,

    -- Facultatif : reste NULL dans les bibliothèques à un seul propriétaire.
    proprietaire_id INTEGER NULL REFERENCES proprietaires(id) ON DELETE SET NULL,
    classement_id  INTEGER NULL REFERENCES classements(id) ON DELETE SET NULL,

    -- Emplacement PHYSIQUE, texte libre. Renseigné en masse par l'import
    -- du scan : c'est le seul renseignement que le physique connaît et
    -- qu'aucune base publique ne donnera.
    rangement      TEXT NOT NULL DEFAULT '',

    annee          INTEGER NULL,
    nb_pages       INTEGER NULL,
    format         TEXT NOT NULL DEFAULT '',
    resume         TEXT NOT NULL DEFAULT '',
    -- Chemin RELATIF au dossier `couvertures/` voisin de la base : le
    -- fichier .db reste petit, l'image se remplace sans y toucher.
    -- ⚠️ Sauvegarder, c'est donc copier DEUX choses.
    couverture     TEXT NOT NULL DEFAULT '',

    -- ----- Ce qui n'appartient qu'à une bibliothèque personnelle
    lu             INTEGER NOT NULL DEFAULT 0,
    date_lecture   TEXT NULL,                  -- ISO-8601 : trie comme chaîne
    appreciation   INTEGER NULL,               -- 1 à 5, NULL = pas d'avis
    commentaire    TEXT NOT NULL DEFAULT '',

    -- ----- Traçabilité de l'enrichissement
    -- D'où vient la notice — dit ce qu'on peut croire.
    source_notice  TEXT NOT NULL DEFAULT '',
    -- Dernière TENTATIVE, réussie ou non : c'est ce qui rend l'enrichissement
    -- reprenable d'un jour sur l'autre. Les quotas gratuits se comptent en
    -- centaines d'appels par jour ; 7 000 fiches s'étalent sur une semaine.
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
--    Van Hamme écrit, Rosiński dessine, et on cherche parfois l'un,
--    parfois l'autre. Un champ texte unique ne permettrait ni de chercher
--    ni de distinguer.
--
--    La table existe d'abord pour l'AUTOCOMPLÉTION : avec ~1 500 albums à
--    saisir à la main, proposer « Rosiński, Grzegorz » sur « Ros… » fait
--    gagner des heures. La recherche par auteur vient en prime.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS livre_auteurs (
    livre_id   INTEGER NOT NULL REFERENCES livres(id)  ON DELETE CASCADE,
    auteur_id  INTEGER NOT NULL REFERENCES auteurs(id) ON DELETE CASCADE,
    -- 'scenario' | 'dessin' | 'couleur' | 'auteur' | 'traduction'
    -- L'import verse tout en 'auteur' : deviner les rôles se tromperait
    -- silencieusement. Ils se corrigent à la main sur ce qui compte.
    role       TEXT NOT NULL DEFAULT 'auteur',
    rang       INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (livre_id, auteur_id, role)
);

CREATE INDEX IF NOT EXISTS idx_livre_auteurs_auteur ON livre_auteurs(auteur_id);
