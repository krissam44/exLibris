using Dapper;
using Exlibris.Data;

namespace Exlibris.Tests;

/// <summary>
/// Les identifiants des référentiels ne doivent pas se consumer.
///
/// <b>Le défaut, mesuré le 29/08/2026 sur SQLite 3.49.</b>
/// <c>INSERT … ON CONFLICT DO NOTHING</c> alloue son identifiant AVANT de
/// découvrir le conflit, et <c>AUTOINCREMENT</c> grave cette allocation dans
/// <c>sqlite_sequence</c> : elle ne se rend jamais. Onze enregistrements d'une
/// fiche dont le genre existait déjà donnaient les identifiants 1 puis 12.
/// D'où les suites 1, 5, 12, 16, 27, 38 relevées dans les référentiels réels.
///
/// <b>Le trou n'est pas le problème</b> — un identifiant de substitution ne se
/// montre à personne, et rien ne dépend de sa continuité. Ce qui est fautif,
/// c'est l'écriture inutile qui le creuse : chaque sauvegarde de fiche tentait
/// cinq INSERT ne créant rien. Les tests portent donc sur la RÉSOLUTION —
/// combien de lignes, quel identifiant — et non sur la beauté de la suite.
///
/// C'est le pendant SQLite du défaut de Lirevia, où MariaDB brûlait ses
/// identifiants sur <c>ON DUPLICATE KEY UPDATE</c>.
/// </summary>
public class IdentifiantsReferentielTests : IDisposable
{
    private readonly BancEssai _banc = new();
    private readonly ReferentielDepot _depot;

    public IdentifiantsReferentielTests() => _depot = new ReferentielDepot(_banc.Fabrique);

    public void Dispose() => _banc.Dispose();

    private long Sequence(string table)
    {
        using var con = _banc.Fabrique.Ouvrir();

        // `sqlite_sequence` ne contient une ligne qu'après la première insertion
        // dans la table : absente, la valeur consommée est nulle.
        return con.ExecuteScalar<long?>(
            "SELECT seq FROM sqlite_sequence WHERE name = @table", new { table }) ?? 0;
    }

    [Fact]
    public void Resoudre_dix_fois_le_meme_libelle_ne_consomme_qu_un_identifiant()
    {
        // LE test. Rejouer la résolution simule ce que fait la saisie : chaque
        // enregistrement d'une fiche résout son genre, son classement, son
        // éditeur, sa série et son propriétaire — que ceux-ci existent déjà ou
        // non.
        var premier = _depot.ResoudreGenre("Roman");

        for (var i = 0; i < 10; i++)
            Assert.Equal(premier, _depot.ResoudreGenre("Roman"));

        Assert.Equal(1L, Sequence("genres"));
        Assert.Single(_depot.Genres());
    }

    [Fact]
    public void Le_libelle_suivant_recoit_l_identifiant_qui_suit()
    {
        // La conséquence visible du test précédent, et celle que l'utilisateur a
        // signalée : sans la lecture préalable, « Policier » arrivait en 12 après
        // onze résolutions de « Roman ».
        _depot.ResoudreGenre("Roman");

        for (var i = 0; i < 10; i++) _depot.ResoudreGenre("Roman");

        var policier = _depot.ResoudreGenre("Policier");

        Assert.Equal(2L, policier);
    }

    [Fact]
    public void La_casse_et_les_accents_ne_creent_pas_d_identifiant_de_plus()
    {
        // Trois écritures du même genre. Le rapprochement se fait sur la clé,
        // donc aucune ne doit ni créer de ligne ni consommer d'identifiant après
        // la première.
        var reference = _depot.ResoudreGenre("Poésie");

        Assert.Equal(reference, _depot.ResoudreGenre("POESIE"));
        Assert.Equal(reference, _depot.ResoudreGenre("poesie"));

        Assert.Equal(1L, Sequence("genres"));
    }

    [Fact]
    public void Chaque_referentiel_est_protege_et_pas_seulement_les_genres()
    {
        // La résolution est mutualisée, mais un correctif posé sur un seul
        // appelant se serait vu ici. Les cinq référentiels que la saisie d'une
        // fiche traverse à chaque enregistrement.
        for (var i = 0; i < 5; i++)
        {
            _depot.ResoudreEditeur("Casterman");
            _depot.ResoudreAuteur("Hergé");
            _depot.ResoudreSerie("Tintin");
            _depot.ResoudreClassement("Les BD de Paul");
            _depot.ResoudreProprietaire("Ma femme");
        }

        Assert.Equal(1L, Sequence("editeurs"));
        Assert.Equal(1L, Sequence("auteurs"));
        Assert.Equal(1L, Sequence("series"));
        Assert.Equal(1L, Sequence("classements"));
        Assert.Equal(1L, Sequence("proprietaires"));
    }

    [Fact]
    public void L_amorcage_des_genres_donne_une_suite_continue()
    {
        // Vingt et un genres semés, donc les identifiants 1 à 21 sans trou.
        // C'est ce que montrait la table de l'utilisateur jusqu'à 21, avant que
        // les résolutions suivantes ne creusent l'écart jusqu'à 58.
        _depot.SemerGenresSiVide();

        var attendus = Enumerable.Range(1, _depot.Genres().Count).Select(i => (long)i);

        using var con = _banc.Fabrique.Ouvrir();
        var identifiants = con.Query<long>("SELECT id FROM genres ORDER BY id").AsList();

        Assert.Equal(attendus, identifiants);
    }

    [Fact]
    public void Une_saisie_vide_ne_touche_a_rien()
    {
        // Un livre peut n'avoir ni éditeur, ni série, ni propriétaire. Le champ
        // laissé vide ne doit créer aucune ligne — ni en consommer une.
        Assert.Null(_depot.ResoudreGenre(""));
        Assert.Null(_depot.ResoudreGenre("   "));
        Assert.Null(_depot.ResoudreGenre(null));

        Assert.Equal(0L, Sequence("genres"));
        Assert.Empty(_depot.Genres());
    }
}
