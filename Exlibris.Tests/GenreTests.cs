using Dapper;
using Exlibris.Data;
using Exlibris.Shared.Modeles;
using Microsoft.Data.Sqlite;

namespace Exlibris.Tests;

/// <summary>
/// Le genre éditeur, et sa cohabitation avec le classement personnel.
///
/// <b>Deux choses valent d'être éprouvées ici</b>, et aucune ne lèverait
/// d'erreur en cédant. D'abord l'aller-retour en base : une colonne oubliée
/// dans l'INSERT ou dans la projection ne casse rien, elle rend simplement le
/// champ vide — c'est le défaut silencieux par excellence, et c'est celui qui a
/// motivé le test du même genre sur les scénarios de DomoMud. Ensuite la
/// migration : elle ne s'exécute que sur une base ANCIENNE, cas que le
/// développeur ne rencontre jamais puisqu'il repart d'une base vierge.
/// </summary>
public class GenreTests : IDisposable
{
    private readonly BancEssai _banc = new();
    private readonly LivreDepot _depot;
    private readonly ReferentielDepot _referentiels;

    public GenreTests()
    {
        _depot = new LivreDepot(_banc.Fabrique);
        _referentiels = new ReferentielDepot(_banc.Fabrique);
    }

    public void Dispose() => _banc.Dispose();

    [Fact]
    public void La_genre_survit_a_l_aller_retour_en_base()
    {
        var id = _depot.Creer(new Livre
        {
            Titre = "La Marque Jaune",
            GenreId = _referentiels.ResoudreGenre("Bande dessinée"),
            ClassementId = _referentiels.ResoudreClassement("Les BD de Paul"),
        });

        var relu = _depot.ParId(id)!;

        // Les deux ensemble, et pas seulement le genre : c'est leur
        // coexistence qui est fragile. Une jointure recopiée depuis celle du
        // classement — le même alias employé deux fois — rendrait le même nom
        // dans les deux champs, et l'erreur passerait inaperçue tant qu'on ne
        // les compare pas.
        Assert.Equal("Bande dessinée", relu.GenreNom);
        Assert.Equal("Les BD de Paul", relu.ClassementNom);
    }

    [Fact]
    public void La_genre_se_modifie_sans_toucher_au_classement()
    {
        var id = _depot.Creer(new Livre
        {
            Titre = "Dune",
            GenreId = _referentiels.ResoudreGenre("Roman"),
            ClassementId = _referentiels.ResoudreClassement("À relire"),
        });

        var livre = _depot.ParId(id)!;
        livre.GenreId = _referentiels.ResoudreGenre("Science-fiction");
        _depot.MettreAJour(livre);

        var relu = _depot.ParId(id)!;
        Assert.Equal("Science-fiction", relu.GenreNom);
        Assert.Equal("À relire", relu.ClassementNom);
    }

    [Fact]
    public void Un_meme_libelle_peut_etre_a_la_fois_genre_et_rayon()
    {
        // Le cas courant : on range ses romans au rayon « Roman ». Les deux
        // référentiels étant distincts, le libellé identique ne doit pas les
        // faire converger vers une seule ligne.
        var genre = _referentiels.ResoudreGenre("Roman");
        var rayon = _referentiels.ResoudreClassement("Roman");

        Assert.NotNull(genre);
        Assert.NotNull(rayon);

        Assert.Single(_referentiels.Genres());
        Assert.Single(_referentiels.Classements());
    }

    [Fact]
    public void Le_genre_se_rapproche_malgre_la_casse_et_les_accents()
    {
        // SQLite ne replie ni la casse au-delà de l'ASCII ni les accents : sans
        // la clé calculée en C#, « poesie » et « Poésie » feraient deux genres
        // distincts dans la liste déroulante.
        var premier = _referentiels.ResoudreGenre("Poésie");
        var second  = _referentiels.ResoudreGenre("POESIE");

        Assert.Equal(premier, second);
        Assert.Single(_referentiels.Genres());
    }

    [Fact]
    public void Mais_la_ponctuation_distingue_encore_deux_genres()
    {
        // ⚠️ LIMITE CONNUE, et vérifiée ici pour qu'elle ne se découvre pas en
        // service. CleCollation ne replie que la casse et les accents : le
        // tiret n'est pas une espace pour elle, donc « Science fiction » tapé à
        // la main crée un SECOND genre à côté du « Science-fiction » semé.
        //
        // Ce n'est pas un oubli mais la prudence assumée de CleReferentiel : un
        // rapprochement plus zélé fusionnerait « Actes Sud » et « Actes-Sud »,
        // ce qu'on veut, mais aussi des libellés qu'il ne faut pas confondre, et
        // rien ne rattraperait la fusion. Le jour où la liste des genres se
        // salira, la réponse est de replier la ponctuation POUR LES CATÉGORIES
        // seulement — pas de toucher à la clé commune.
        var avecTiret = _referentiels.ResoudreGenre("Science-fiction");
        var avecEspace = _referentiels.ResoudreGenre("Science fiction");

        Assert.NotEqual(avecTiret, avecEspace);
        Assert.Equal(2, _referentiels.Genres().Count);
    }

    [Fact]
    public void La_nomenclature_ne_se_seme_qu_une_fois()
    {
        _referentiels.SemerGenresSiVide();
        var apresPremierSemis = _referentiels.Genres().Count;
        Assert.True(apresPremierSemis > 0);

        // On retire un genre, comme le ferait quelqu'un qui ne lit pas de manga.
        using (var con = _banc.Fabrique.Ouvrir())
            con.Execute("DELETE FROM genres WHERE nom = 'Manga'");

        _referentiels.SemerGenresSiVide();

        // Il ne revient PAS. Un amorçage rejoué à chaque démarrage ferait
        // réapparaître le genre supprimé, et l'utilisateur conclurait que
        // l'application ne l'écoute pas.
        Assert.Equal(apresPremierSemis - 1, _referentiels.Genres().Count);
        Assert.DoesNotContain(_referentiels.Genres(), c => c.Nom == "Manga");
    }

    [Fact]
    public void La_migration_ajoute_la_colonne_a_une_base_ancienne()
    {
        // Une base d'AVANT le genre : la table `livres` existe sans la
        // colonne. C'est l'état de la base de l'utilisateur, et le seul où la
        // migration sert à quelque chose — le schéma complet, lui, crée déjà
        // la colonne, si bien qu'un test sur base neuve ne prouverait rien.
        var dossier = Path.Combine(Path.GetTempPath(), "exlibris-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);

        try
        {
            var fabrique = new FabriqueConnexion(Path.Combine(dossier, "ancienne.db"), regroupement: false);

            using (var con = fabrique.Ouvrir())
            {
                con.Execute("""
                    CREATE TABLE genres (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        nom TEXT NOT NULL,
                        cle_recherche TEXT NOT NULL UNIQUE);
                    CREATE TABLE livres (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        titre TEXT NOT NULL);
                    """);

                Assert.DoesNotContain(Colonnes(con, "livres"), c => c == "genre_id");
                Assert.DoesNotContain(Colonnes(con, "livres"), c => c == "proprietaire_id");
            }

            fabrique.AppliquerMigrations(Schema.Migrations());

            using (var con = fabrique.Ouvrir())
                Assert.Contains(Colonnes(con, "livres"), c => c == "genre_id");

            // Rejouée, elle ne doit pas lever : c'est le cas de TOUS les
            // démarrages suivants, donc le cas normal et non l'exception.
            fabrique.AppliquerMigrations(Schema.Migrations());

            using (var con = fabrique.Ouvrir())
                Assert.Contains(Colonnes(con, "livres"), c => c == "genre_id");
        }
        finally
        {
            try { Directory.Delete(dossier, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Une_base_de_la_version_precedente_demarre_sans_erreur()
    {
        // LE test de cette classe, et celui qui manquait : la séquence EXACTE du
        // démarrage — schéma puis migrations — jouée sur une base d'avant la
        // genre.
        //
        // Le test de migration ci-dessus n'appelait qu'AppliquerMigrations sur
        // une table fabriquée à la main. Il passait donc pendant que le
        // démarrage réel plantait : le schéma, qui s'exécute EN PREMIER,
        // contenait un CREATE INDEX sur une colonne que seule la migration
        // suivante allait créer. Base neuve indemne, base de l'utilisateur
        // inutilisable — et rien pour le dire avant le lancement.
        var dossier = Path.Combine(Path.GetTempPath(), "exlibris-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);

        try
        {
            var fabrique = new FabriqueConnexion(Path.Combine(dossier, "version-precedente.db"), regroupement: false);

            // Une base de la version d'AVANT : le schéma complet, dont on retire
            // ce que le genre a apporté. Fabriquer l'ancien schéma en le
            // recopiant le figerait ; le déduire du schéma courant le garde
            // juste au fil des versions.
            fabrique.CreerSiNecessaire(Schema.Sql());
            using (var con = fabrique.Ouvrir())
            {
                con.Execute("DROP INDEX IF EXISTS idx_livres_genre");
                con.Execute("DROP INDEX IF EXISTS idx_livres_proprietaire");
                con.Execute("ALTER TABLE livres DROP COLUMN genre_id");
                con.Execute("ALTER TABLE livres DROP COLUMN proprietaire_id");
                con.Execute(
                    "INSERT INTO livres (titre, cle_recherche) VALUES ('La vie est un roman', 'la vie est un roman')");
            }

            // La séquence de App.OnStartup, dans son ordre.
            fabrique.CreerSiNecessaire(Schema.Sql());
            fabrique.AppliquerMigrations(Schema.Migrations());

            using (var con = fabrique.Ouvrir())
            {
                Assert.Contains(Colonnes(con, "livres"), c => c == "genre_id");

                // Chaque colonne ajoutée depuis doit être reprise ici : c'est le
                // seul test qui parcourt le chemin de mise à niveau, et une
                // migration oubliée n'échouerait nulle part ailleurs.
                Assert.Contains(Colonnes(con, "livres"), c => c == "proprietaire_id");

                // L'index aussi : posé par la migration, il ne doit pas s'être
                // perdu en chemin quand on l'a sorti du schéma.
                Assert.Equal(1L, con.ExecuteScalar<long>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='idx_livres_genre'"));
            }

            // Et le livre déjà saisi se relit, sans genre — ce qui est normal.
            var relu = new LivreDepot(fabrique).Rechercher();
            Assert.Equal("La vie est un roman", Assert.Single(relu).Titre);
            Assert.Equal(string.Empty, relu[0].GenreNom);
        }
        finally
        {
            try { Directory.Delete(dossier, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Une_base_au_VOCABULAIRE_d_avant_se_renomme_sans_rien_perdre()
    {
        // Le chemin de la base RÉELLE au 30/08/2026 : elle porte encore la table
        // « categories » et la colonne « categorie_id ». Le renommage doit la
        // reprendre sans perdre un livre, un genre, ni un alias de fusion.
        //
        // C'est le seul test qui parcourt ce chemin. Une étape oubliée ne se
        // verrait nulle part ailleurs : l'application interrogerait « genres »
        // pendant que la base garderait « categories », et tous les genres
        // arriveraient vides.
        var dossier = Path.Combine(Path.GetTempPath(), "exlibris-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);

        try
        {
            var fabrique = new FabriqueConnexion(Path.Combine(dossier, "ancienne.db"), regroupement: false);

            // La base d'avant : on part du schéma courant, puis on remet le
            // vocabulaire précédent. Fabriquer l'ancien schéma à la main le
            // figerait ; le déduire du courant le garde juste au fil des versions.
            fabrique.CreerSiNecessaire(Schema.Sql());
            fabrique.AppliquerMigrations(Schema.Migrations());

            using (var con = fabrique.Ouvrir())
            {
                con.Execute("DROP INDEX IF EXISTS idx_livres_genre");
                con.Execute("ALTER TABLE genres RENAME TO categories");
                con.Execute("ALTER TABLE livres RENAME COLUMN genre_id TO categorie_id");

                con.Execute("""
                    INSERT INTO categories (id, nom, cle_recherche) VALUES (1, 'Roman', 'roman');
                    INSERT INTO livres (titre, cle_recherche, categorie_id, rangement)
                    VALUES ('Quelqu''un d''autre', 'quelqu''un d''autre', 1, 'Salon');
                    INSERT INTO alias_referentiel (referentiel, cle_recherche, cible_id)
                    VALUES ('categories', 'romans', 1);
                    """);

                Assert.Contains(Colonnes(con, "livres"), c => c == "categorie_id");
            }

            // La séquence de App.OnStartup, telle quelle — renommages d'abord.
            fabrique.AppliquerMigrations(Schema.MigrationsPrealables());
            fabrique.CreerSiNecessaire(Schema.Sql());
            fabrique.AppliquerMigrations(Schema.Migrations());

            using (var con = fabrique.Ouvrir())
            {
                Assert.Contains(Colonnes(con, "livres"), c => c == "genre_id");
                Assert.DoesNotContain(Colonnes(con, "livres"), c => c == "categorie_id");

                // Le genre suit son livre.
                Assert.Equal("Roman", con.ExecuteScalar<string>(
                    "SELECT g.nom FROM livres l JOIN genres g ON g.id = l.genre_id"));

                // Et l'alias de fusion aussi : oublié, un libellé rapproché à la
                // main reviendrait à la première notice.
                Assert.Equal("genres", con.ExecuteScalar<string>(
                    "SELECT referentiel FROM alias_referentiel WHERE cle_recherche = 'romans'"));
            }

            // Rejouée, la migration ne doit rien casser : c'est le cas de tous les
            // démarrages suivants.
            fabrique.AppliquerMigrations(Schema.Migrations());

            using (var con = fabrique.Ouvrir())
                Assert.Equal(1, con.ExecuteScalar<int>("SELECT COUNT(*) FROM livres"));
        }
        finally
        {
            try { Directory.Delete(dossier, recursive: true); } catch (IOException) { }
        }
    }

    private static List<string> Colonnes(SqliteConnection con, string table) =>
        con.Query<string>($"SELECT name FROM pragma_table_info('{table}')").AsList();
}
