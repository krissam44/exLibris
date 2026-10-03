using Dapper;
using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// Le dépôt des ouvrages.
///
/// <b>Ce qui vaut d'être éprouvé n'est pas l'INSERT</b> — Dapper s'en charge —
/// mais les trois règles qui, si elles cèdent, ne lèvent aucune erreur : l'ISBN
/// vide écrit en <c>NULL</c>, la clé de recherche qui replie ce que SQLite ne
/// replie pas, et le doublon signalé sans être interdit.
/// </summary>
public class LivreDepotTests : IDisposable
{
    private readonly BancEssai _banc = new();
    private readonly LivreDepot _depot;
    private readonly ReferentielDepot _referentiels;

    public LivreDepotTests()
    {
        _depot = new LivreDepot(_banc.Fabrique);
        _referentiels = new ReferentielDepot(_banc.Fabrique);
    }

    public void Dispose() => _banc.Dispose();

    private static Livre Album(string titre, string isbn = "") =>
        new() { Titre = titre, Isbn = isbn };

    [Fact]
    public void Un_livre_saisi_a_la_main_se_relit_entier()
    {
        // Le chemin des ~1 500 albums sans code-barres : aucun ISBN, tout à la
        // main. C'est le cas NORMAL ici, pas un repli.
        var id = _depot.Creer(new Livre
        {
            Titre = "La Marque Jaune",
            Rangement = "Salle 1, étagère B",
            Annee = 1956,
            Format = "Relié",
            Lu = true,
            Appreciation = 5,
            Commentaire = "Le meilleur Blake et Mortimer.",
        });

        var relu = _depot.ParId(id);

        Assert.NotNull(relu);
        Assert.Equal("La Marque Jaune", relu!.Titre);
        Assert.Equal("Salle 1, étagère B", relu.Rangement);
        Assert.Equal(1956, relu.Annee);
        Assert.True(relu.Lu);
        Assert.Equal(5, relu.Appreciation);
    }

    [Fact]
    public void Un_ISBN_absent_est_ecrit_NULL_et_non_chaine_vide()
    {
        // ⚠️ La règle qui ne lève jamais d'erreur si on l'oublie. En chaîne
        // vide, les ~1 500 albums sans code-barres se ressembleraient tous : le
        // contrôle de doublon les signalerait comme un seul et même livre.
        _depot.Creer(Album("Album ancien A"));
        _depot.Creer(Album("Album ancien B"));

        using var con = _banc.Fabrique.Ouvrir();
        Assert.Equal(2, con.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM livres WHERE isbn IS NULL"));
        Assert.Equal(0, con.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM livres WHERE isbn = ''"));
    }

    [Fact]
    public void Le_code_additionnel_d_un_album_est_retire_a_l_ecriture()
    {
        // La normalisation vit dans le dépôt et pas seulement dans l'écran : un
        // import CSV n'ouvre aucun écran, et c'est lui qui verra le plus de
        // codes-barres mal lus.
        var id = _depot.Creer(Album("Thorgal, tome 1", "978280368003001234"));

        Assert.Equal("9782803680030", _depot.ParId(id)!.Isbn);
    }

    [Fact]
    public void Un_doublon_d_ISBN_est_SIGNALE_et_non_refuse()
    {
        // Deux cadeaux identiques : exceptionnel, mais possible. La base
        // l'accepte ; c'est l'écran qui demandera s'il s'agit d'un second
        // exemplaire ou d'une erreur de scan.
        _depot.Creer(Album("Thorgal, tome 1", "9782803680030"));
        _depot.Creer(Album("Thorgal, tome 1", "9782803680030"));

        var doublons = _depot.ParIsbn("978-2-8036-8003-0");

        Assert.Equal(2, doublons.Count);
    }

    [Fact]
    public void La_recherche_ignore_la_casse_ET_LES_ACCENTS()
    {
        // ⚠️ Ce que SQLite ne sait pas faire seul. Sa collation NOCASE ne replie
        // que l'ASCII : sans la clé calculée en C#, « litterature » ne
        // trouverait pas « Littérature », et l'utilisateur conclurait que son
        // livre n'est pas au catalogue.
        _depot.Creer(Album("Histoire de la Littérature française"));

        Assert.Single(_depot.Rechercher("littérature"));
        Assert.Single(_depot.Rechercher("LITTERATURE"));
        Assert.Single(_depot.Rechercher("litterature"));
    }

    [Fact]
    public void La_recherche_trouve_par_ISBN_meme_ponctue()
    {
        // Le chemin de la douchette, et celui du copier-coller depuis un site
        // marchand — qui ponctue presque toujours.
        _depot.Creer(Album("Thorgal, tome 1", "9782803680030"));

        Assert.Single(_depot.Rechercher("978-2-8036-8003-0"));
        Assert.Single(_depot.Rechercher("9782803680030"));
    }

    [Fact]
    public void La_recherche_trouve_par_SERIE_et_par_AUTEUR()
    {
        // On cherche « Thorgal » sans savoir le titre du tome, et « Rosiński »
        // sans savoir lequel de ses albums on possède. Chercher sur le seul
        // titre manquerait les deux.
        var serie = _referentiels.ResoudreSerie("Thorgal");
        var auteur = _referentiels.ResoudreAuteur("Rosiński, Grzegorz");

        var id = _depot.Creer(new Livre
        {
            Titre = "La magicienne trahie", SerieId = serie, NumeroTome = 1,
        });
        _depot.DefinirAuteurs(id, [(auteur!.Value, RoleAuteur.Dessin)]);

        Assert.Single(_depot.Rechercher("thorgal"));
        Assert.Single(_depot.Rechercher("rosinski"));   // sans le ń
        Assert.Single(_depot.Rechercher("magicienne"));
    }

    [Fact]
    public void Les_livres_sortent_TRIES_par_serie_puis_par_tome()
    {
        // L'ordre attendu d'une bibliothèque de bandes dessinées. Un tri sur le
        // seul titre placerait « La magicienne trahie » avant « Almées », c'est-
        // à-dire le tome 1 après le tome 10.
        var serie = _referentiels.ResoudreSerie("Thorgal");

        foreach (var (tome, titre) in new[] { (10, "Le pays Qâ"), (1, "La magicienne trahie"), (2, "L'île des mers gelées") })
            _depot.Creer(new Livre { Titre = titre, SerieId = serie, NumeroTome = tome });

        var tomes = _depot.Rechercher().Select(l => l.NumeroTome).ToList();

        Assert.Equal([1, 2, 10], tomes);
    }

    [Fact]
    public void Le_titre_complet_porte_la_serie_et_le_tome()
    {
        // C'est ce que la colonne « Titre » affiche. « La magicienne trahie »
        // seul ne dit ni la série ni le rang — sur 5 000 albums, la liste
        // devient illisible.
        var serie = _referentiels.ResoudreSerie("Thorgal");
        var id = _depot.Creer(new Livre { Titre = "La magicienne trahie", SerieId = serie, NumeroTome = 1 });

        Assert.Equal("Thorgal — 1. La magicienne trahie", _depot.ParId(id)!.TitreComplet);

        // Hors-série : pas de numéro, mais la série reste nommée.
        var hs = _depot.Creer(new Livre { Titre = "Kriss de Valnor", SerieId = serie });
        Assert.Equal("Thorgal — Kriss de Valnor", _depot.ParId(hs)!.TitreComplet);

        // Roman isolé : le titre seul, sans tiret orphelin.
        var roman = _depot.Creer(Album("Dune"));
        Assert.Equal("Dune", _depot.ParId(roman)!.TitreComplet);
    }

    [Fact]
    public void Supprimer_un_livre_emporte_ses_auteurs()
    {
        // La cascade ne fonctionne QUE si les clés étrangères sont actives —
        // ce dont FabriqueConnexion se charge à chaque ouverture. Sans elles,
        // les lignes d'auteurs survivraient, orphelines et invisibles.
        var auteur = _referentiels.ResoudreAuteur("Van Hamme, Jean");
        var id = _depot.Creer(Album("Largo Winch, tome 1"));
        _depot.DefinirAuteurs(id, [(auteur!.Value, RoleAuteur.Scenario)]);

        Assert.True(_depot.Supprimer(id));

        using var con = _banc.Fabrique.Ouvrir();
        Assert.Equal(0, con.ExecuteScalar<int>("SELECT COUNT(*) FROM livre_auteurs"));
        // L'auteur, lui, reste : il sert peut-être à d'autres albums, et le
        // supprimer viderait l'autocomplétion à chaque désherbage.
        Assert.Equal(1, con.ExecuteScalar<int>("SELECT COUNT(*) FROM auteurs"));
    }
}

/// <summary>
/// Les référentiels alimentés au fil de la saisie.
/// </summary>
public class ReferentielDepotTests : IDisposable
{
    private readonly BancEssai _banc = new();
    private readonly ReferentielDepot _depot;

    public ReferentielDepotTests() => _depot = new ReferentielDepot(_banc.Fabrique);

    public void Dispose() => _banc.Dispose();

    [Fact]
    public void Le_meme_libelle_ne_cree_qu_une_seule_entree()
    {
        var a = _depot.ResoudreEditeur("Le Lombard");
        var b = _depot.ResoudreEditeur("Le Lombard");

        Assert.Equal(a, b);
        Assert.Single(_depot.Editeurs());
    }

    [Fact]
    public void La_casse_ET_LES_ACCENTS_convergent_vers_la_meme_entree()
    {
        // ⚠️ LE test de ce fichier. C'est la clé calculée en C# qui replie, pas
        // SQLite — dont la collation NOCASE laisse « Éditions » et « editions »
        // comme deux entrées distinctes. Sans elle, la liste des éditeurs se
        // remplirait de variantes que personne ne remarquerait avant d'en avoir
        // trente.
        var a = _depot.ResoudreEditeur("Éditions Dupuis");
        var b = _depot.ResoudreEditeur("éditions dupuis");
        var c = _depot.ResoudreEditeur("EDITIONS DUPUIS");

        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.Single(_depot.Editeurs());
    }

    [Fact]
    public void Le_prefixe_d_usage_ne_cree_pas_un_second_editeur()
    {
        // CleReferentiel.Editeur retire « Éditions » en tête : « Éditions
        // Gallimard » et « Gallimard » sont la même maison. Règle reprise de
        // Lirevia, où elle réglait le même problème sur un référentiel partagé.
        var a = _depot.ResoudreEditeur("Gallimard");
        var b = _depot.ResoudreEditeur("Éditions Gallimard");

        // Ce test a longtemps affirmé l'INVERSE de son nom, avec un commentaire
        // expliquant que la résolution employait CleCollation — laquelle replie
        // la casse et les accents, mais pas les préfixes. Il constatait donc un
        // écart entre l'intention et le code : mieux qu'un test muet, mais un
        // aveu.
        //
        // L'écart est comblé depuis le 29/08/2026 : les éditeurs passent par
        // CleReferentiel.Editeur, et le test dit enfin ce que son nom annonce.
        Assert.Equal(a, b);
        Assert.Single(_depot.Editeurs());

        // La contrepartie, et la raison pour laquelle ce repli reste TIMIDE : un
        // qualificatif distingue toujours deux maisons. Sans cette limite, la
        // même règle avalerait des catalogues entiers.
        Assert.NotEqual(a, _depot.ResoudreEditeur("Gallimard Jeunesse"));
    }

    [Fact]
    public void Une_saisie_vide_ne_cree_aucune_entree()
    {
        // Un livre peut n'avoir ni éditeur, ni série, ni classement. Créer une
        // entrée vide polluerait chaque liste déroulante d'un premier élément
        // invisible et non sélectionnable.
        Assert.Null(_depot.ResoudreEditeur(""));
        Assert.Null(_depot.ResoudreEditeur("   "));
        Assert.Null(_depot.ResoudreEditeur(null));
        Assert.Empty(_depot.Editeurs());
    }

    [Fact]
    public void Les_series_rapportent_ce_qu_il_faut_pour_reperer_les_trous()
    {
        var serie = _depot.ResoudreSerie("Thorgal");
        var livres = new LivreDepot(_banc.Fabrique);

        foreach (var tome in new[] { 1, 2, 3, 5 })
            livres.Creer(new Livre { Titre = $"Tome {tome}", SerieId = serie, NumeroTome = tome });

        var thorgal = Assert.Single(_depot.Series());

        Assert.Equal(4, thorgal.NbPossedes);
        Assert.Equal(5, thorgal.TomeMax);   // le 4 manque : 4 possédés, max à 5
    }
}
