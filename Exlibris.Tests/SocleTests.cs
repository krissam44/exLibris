using Dapper;
using Exlibris.Shared.Modeles;
using Microsoft.Data.Sqlite;

namespace Exlibris.Tests;

/// <summary>
/// Le socle : le schéma s'applique, les clés étrangères mordent, et les deux
/// règles reprises de Lirevia se comportent comme prévu SUR SQLITE.
///
/// Ces tests ne valident aucune fonctionnalité — ils valident que la chaîne
/// tient avant qu'on écrive le premier écran.
/// </summary>
public class SocleTests : IDisposable
{
    private readonly BancEssai _banc = new();

    /// <summary>
    /// ⚠️ UNE BASE NEUVE PAR TEST, et non par classe.
    ///
    /// Le banc était d'abord partagé via <c>IClassFixture</c> : les tests
    /// écrivaient tous dans le même fichier, et
    /// <c>Plusieurs_livres_peuvent_n_avoir_AUCUN_isbn</c> comptait aussi les
    /// quatre tomes insérés par le test des séries. Il passait seul et échouait
    /// en suite — le défaut le plus coûteux à diagnostiquer.
    ///
    /// xUnit instancie la classe UNE FOIS PAR MÉTHODE : créer le banc dans le
    /// champ suffit donc à isoler chaque test. C'est un luxe que SQLite permet
    /// et que MySQL n'aurait pas — le banc de Lirevia doit tronquer ses tables
    /// entre deux tests parce qu'y créer une base coûterait des secondes ; ici
    /// c'est un fichier dans le dossier temporaire.
    /// </summary>
    public void Dispose() => _banc.Dispose();

    [Fact]
    public void Le_schema_cree_toutes_les_tables()
    {
        using var con = _banc.Fabrique.Ouvrir();

        var tables = con.Query<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name").ToList();

        Assert.Contains("livres", tables);
        Assert.Contains("series", tables);
        Assert.Contains("auteurs", tables);
        Assert.Contains("editeurs", tables);
        Assert.Contains("classements", tables);
        Assert.Contains("livre_auteurs", tables);
    }

    [Fact]
    public void Les_cles_etrangeres_sont_ACTIVES_sur_la_connexion()
    {
        // ⚠️ LE test de ce fichier. SQLite désactive les clés étrangères par
        // défaut, et le fait sans rien dire : une contrainte déclarée au schéma
        // mais jamais appliquée. Un livre rattaché à une série inexistante
        // passerait sans un mot, et la base se dégraderait à bas bruit.
        //
        // Le PRAGMA du schéma ne vaut que pour la connexion qui l'a exécuté —
        // c'est pourquoi FabriqueConnexion le rejoue à chaque ouverture. Ce
        // test est ce qui empêche de l'y retirer par inadvertance.
        using var con = _banc.Fabrique.Ouvrir();

        Assert.Equal(1, con.ExecuteScalar<int>("PRAGMA foreign_keys"));

        var erreur = Assert.Throws<SqliteException>(() => con.Execute(
            "INSERT INTO livres (titre, cle_recherche, serie_id) VALUES ('Orphelin', 'orphelin', 9999)"));

        Assert.Contains("FOREIGN KEY", erreur.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deux_livres_peuvent_porter_le_MEME_isbn()
    {
        // Deux cadeaux identiques : exceptionnel, mais possible. C'est ce qui a
        // fait retirer l'UNIQUE — le doublon est signalé à la saisie, jamais
        // interdit par la base. À l'inverse de Lirevia, dont l'unicité protège
        // un référentiel partagé entre bibliothèques.
        using var con = _banc.Fabrique.Ouvrir();

        for (var i = 0; i < 2; i++)
            con.Execute("""
                INSERT INTO livres (isbn, titre, cle_recherche)
                VALUES ('9782803680030', 'Thorgal, tome 1', 'thorgal tome 1')
                """);

        Assert.Equal(2, con.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM livres WHERE isbn = '9782803680030'"));
    }

    [Fact]
    public void Plusieurs_livres_peuvent_n_avoir_AUCUN_isbn()
    {
        // Environ 30 % des albums, soit ~1 500 sur le fonds visé. Ils portent
        // NULL et jamais une chaîne vide : sinon ils se ressembleraient tous au
        // moment de chercher un doublon.
        using var con = _banc.Fabrique.Ouvrir();

        foreach (var titre in new[] { "Album ancien A", "Album ancien B", "Album ancien C" })
            con.Execute(
                "INSERT INTO livres (isbn, titre, cle_recherche) VALUES (NULL, @titre, @cle)",
                new { titre, cle = CleReferentiel.CleCollation(titre) });

        Assert.Equal(3, con.ExecuteScalar<int>("SELECT COUNT(*) FROM livres WHERE isbn IS NULL"));
    }

    [Fact]
    public void La_cle_de_recherche_replie_les_accents_LA_OU_SQLITE_NE_LE_FAIT_PAS()
    {
        // ⚠️ LA décision structurante du modèle, et sa démonstration.
        //
        // SQLite NOCASE ne replie que l'ASCII : « Litterature » et
        // « LITTERATURE » se rejoignent, « Littérature » reste séparée. En
        // s'en remettant à la base, on obtiendrait deux rayons distincts sans
        // que personne s'en aperçoive avant d'en avoir trente.
        //
        // C'est l'inverse du piège de Lirevia, où MariaDB repliait les accents
        // pendant que le C# ne le faisait pas — 170 000 identifiants brûlés.
        using var con = _banc.Fabrique.Ouvrir();

        // D'abord la démonstration du défaut : la base, seule, ne suffit pas.
        con.Execute("CREATE TEMP TABLE essai (nom TEXT COLLATE NOCASE)");
        con.Execute("INSERT INTO essai VALUES ('Litterature'), ('Littérature')");

        Assert.Equal(1, con.ExecuteScalar<int>("SELECT COUNT(*) FROM essai WHERE nom = 'litterature'"));

        // Puis la parade : c'est le C# qui replie, et la clé qui porte l'unicité.
        con.Execute(
            "INSERT INTO classements (nom, cle_recherche) VALUES (@nom, @cle)",
            new { nom = "Littérature", cle = CleReferentiel.CleCollation("Littérature") });

        var collision = Assert.Throws<SqliteException>(() => con.Execute(
            "INSERT INTO classements (nom, cle_recherche) VALUES (@nom, @cle)",
            new { nom = "LITTERATURE", cle = CleReferentiel.CleCollation("LITTERATURE") }));

        Assert.Contains("UNIQUE", collision.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Les_tomes_se_trient_en_ORDRE_NUMERIQUE()
    {
        // La raison d'être du type INTEGER sur numero_tome. En texte, le tome 10
        // se placerait entre le 1 et le 2 — sur une série de vingt albums, la
        // liste devient inutilisable.
        using var con = _banc.Fabrique.Ouvrir();

        var serie = con.ExecuteScalar<long>("""
            INSERT INTO series (nom, cle_recherche) VALUES ('Thorgal', 'thorgal');
            SELECT last_insert_rowid();
            """);

        foreach (var tome in new[] { 10, 2, 1, 11 })
            con.Execute("""
                INSERT INTO livres (titre, cle_recherche, serie_id, numero_tome)
                VALUES (@titre, @cle, @serie, @tome)
                """,
                new { titre = $"Thorgal, tome {tome}", cle = $"thorgal tome {tome}", serie, tome });

        var ordre = con.Query<int>(
            "SELECT numero_tome FROM livres WHERE serie_id = @serie ORDER BY numero_tome",
            new { serie }).ToList();

        Assert.Equal([1, 2, 10, 11], ordre);
    }
}

/// <summary>
/// La normalisation d'un code-barres tel qu'il sort d'une douchette.
///
/// Sans base : c'est la règle qui mérite d'être éprouvée, et elle décide de la
/// réussite d'une reprise de 7 000 ouvrages.
/// </summary>
public class IsbnTests
{
    [Theory]
    [InlineData("978-2-8036-8003-0", "9782803680030")]
    [InlineData("  9782803680030  ", "9782803680030")]
    [InlineData("2-266-11156-6", "2266111566")]
    public void Les_separateurs_de_saisie_sautent(string saisie, string attendu) =>
        Assert.Equal(attendu, Isbn.Normaliser(saisie));

    [Fact]
    public void Le_code_additionnel_des_albums_est_retire()
    {
        // ⚠️ Le piège du scan de BD. Beaucoup d'albums portent, à droite de
        // l'EAN-13, un code de 2 ou 5 chiffres — prix, numéro de série. Certaines
        // douchettes le concatènent. Le résultat n'est pas invalide au sens du
        // scanner : il est simplement faux, et aucune base ne le connaîtra.
        Assert.Equal("9782803680030", Isbn.Normaliser("978280368003001234"));   // +5
        Assert.Equal("9782803680030", Isbn.Normaliser("978280368003012"));      // +2
    }

    [Fact]
    public void Une_saisie_simplement_fausse_n_est_PAS_tronquee()
    {
        // La coupe n'a lieu que si les treize premiers chiffres forment un EAN
        // VALIDE. Sans cette condition, on rendrait plausible une saisie
        // erronée en la raccourcissant — un faux qui ressemble à un vrai est
        // pire qu'un faux qui se voit.
        const string faux = "111111111111111";   // 15 chiffres, clé fausse
        Assert.Equal(faux, Isbn.Normaliser(faux));
    }

    [Theory]
    [InlineData("9782803680030")]   // ISBN-13
    [InlineData("2266111566")]      // ISBN-10
    [InlineData("2-7654-1005-4")]   // ISBN-10 ponctué
    public void Une_cle_de_controle_juste_est_reconnue(string saisie) =>
        Assert.True(Isbn.EstValide(saisie), $"« {saisie} » aurait dû être valide.");

    [Theory]
    [InlineData("9782803680031")]   // dernier chiffre modifié
    [InlineData("978280368003")]    // tronqué
    [InlineData("")]
    [InlineData(null)]
    public void Une_saisie_douteuse_est_signalee(string? saisie) =>
        Assert.False(Isbn.EstValide(saisie));

    [Fact]
    public void Le_X_de_controle_d_un_ISBN_10_est_conserve()
    {
        // Il vaut 10 et n'apparaît qu'en dernière position. Le retirer avec les
        // autres lettres rendrait invalides tous les ISBN-10 qui le portent —
        // c'est-à-dire un sur onze, soit une part notable d'un fonds ancien.
        // 0-8044-2957-X est un ISBN-10 authentique. Le premier essai de ce test
        // employait un code INVENTÉ, dont la clé ne tombait pas juste : le test
        // échouait alors que le code avait raison. Une donnée d'essai fabriquée
        // à la main doit être vérifiée comme le reste.
        Assert.Equal("080442957X", Isbn.Normaliser("0-8044-2957-X"));
        Assert.True(Isbn.EstValide("0-8044-2957-X"));
    }
}
