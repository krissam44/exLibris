using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// La correction en lot (12/11/2026) : série et propriétaire posés sur
/// plusieurs ouvrages d'un coup, et la série d'Open Library rendue à son champ.
///
/// <b>Le défaut d'origine</b> : « series:Astérix », premier sujet que rend
/// Open Library, devenait le GENRE de l'album. 104 ouvrages, 4 séries.
/// </summary>
public class CorrectionEnLotTests : IDisposable
{
    private readonly BancEssai _banc = new();
    private readonly LivreDepot _livres;
    private readonly ReferentielDepot _referentiels;

    public CorrectionEnLotTests()
    {
        _livres = new LivreDepot(_banc.Fabrique);
        _referentiels = new ReferentielDepot(_banc.Fabrique);
    }

    public void Dispose() => _banc.Dispose();

    // -----------------------------------------------------------------------------------------------------------------
    //  Open Library
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Extrait de la vraie réponse pour 9782012101524, relevée le 12/11/2026.</summary>
    private const string AsterixEnCorse = """
        {"ISBN:9782012101524":{"title":"Astérix en Corse",
          "subjects":[{"name":"series:Astérix"},{"name":"Children's stories"},{"name":"Graphic novels"},
                      {"name":"Spanish: Grades 2-3"}]}}
        """;

    [Fact]
    public void Open_Library_rend_la_serie_dans_son_champ_et_un_vrai_genre()
    {
        var notice = NoticeEnLigneService.AnalyserOpenLibrary(AsterixEnCorse, "9782012101524")!;

        Assert.Equal("Astérix", notice.Serie);
        Assert.Equal("Children's stories", notice.Genre);
    }

    [Theory]
    [InlineData("series:Astérix", true)]
    [InlineData("place:corsica", true)]
    [InlineData("Spanish: Grades 2-3", false)]
    [InlineData("Comic books, strips", false)]
    public void Une_etiquette_est_un_mot_minuscule_colle_a_deux_points(string sujet, bool etiquette) =>
        Assert.Equal(etiquette, NoticeEnLigneService.EstEtiquette(sujet));

    [Fact]
    public async Task La_completion_par_lot_pose_la_serie_d_Open_Library()
    {
        var inventaire = new InventaireDepot(_banc.Fabrique);
        inventaire.Importer([new LigneInventaire { Isbn = "9782012101524" }]);

        await new CompletionParLot(inventaire, _livres, _referentiels,
                chercher: (isbn, _) => Task.FromResult(NoticeEnLigneService.AnalyserOpenLibrary(AsterixEnCorse, isbn)),
                rapporterCouverture: (_, _) => Task.FromResult(string.Empty))
            { Pause = TimeSpan.Zero }
            .TraiterAsync();

        var livre = _livres.ParIsbn("9782012101524").Single();
        Assert.Equal("Astérix", livre.SerieNom);
        Assert.Equal("Children's stories", livre.GenreNom);
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  Réparation de l'existant
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void La_reparation_rend_la_serie_et_retire_le_faux_genre()
    {
        var faux = _referentiels.ResoudreGenre("series:Astérix");
        var sansSerie = _livres.Creer(new Livre { Titre = "Astérix en Corse", GenreId = faux });
        var avecSerie = _livres.Creer(new Livre
        {
            Titre = "Astérix chez les Belges", GenreId = faux,
            SerieId = _referentiels.ResoudreSerie("Les aventures d'Astérix"),
        });

        Assert.Equal(1, _referentiels.ReparerEtiquettesOpenLibrary());

        var corse = _livres.ParId(sansSerie)!;
        Assert.Equal("Astérix", corse.SerieNom);
        Assert.Equal(string.Empty, corse.GenreNom);

        // Une série déjà posée — peut-être à la main — n'est pas remplacée.
        Assert.Equal("Les aventures d'Astérix", _livres.ParId(avecSerie)!.SerieNom);
        Assert.DoesNotContain(_referentiels.Genres(), g => g.Nom.StartsWith("series:"));

        // Idempotente : rien à refaire au démarrage suivant.
        Assert.Equal(0, _referentiels.ReparerEtiquettesOpenLibrary());
    }

    [Fact]
    public void La_reparation_nettoie_aussi_ma_categorie()
    {
        // « Ma catégorie » recopie le genre à la saisie : le faux genre a pu y passer.
        var id = _livres.Creer(new Livre
        {
            Titre = "Astérix en Corse",
            ClassementId = _referentiels.ResoudreClassement("series:Astérix"),
        });

        _referentiels.ReparerEtiquettesOpenLibrary();

        var livre = _livres.ParId(id)!;
        Assert.Equal(string.Empty, livre.ClassementNom);
        Assert.Equal("Astérix", livre.SerieNom);
    }

    [Fact]
    public void Les_series_se_lisent_quand_une_serie_sans_tome_en_precede_une_numerotee()
    {
        // Le cas exact du 12/11/2026 : la réparation crée « Astérix » sans tome
        // numéroté, classée avant « Thorgal » qui en a. Dapper figeait le type
        // du MAX() sur la première ligne (NULL) et échouait sur la seconde.
        _livres.Creer(new Livre { Titre = "Astérix en Corse", SerieId = _referentiels.ResoudreSerie("Astérix") });
        _livres.Creer(new Livre { Titre = "Thorgal 9", SerieId = _referentiels.ResoudreSerie("Thorgal"), NumeroTome = 9 });

        var series = _referentiels.Series();

        Assert.Null(series.Single(s => s.Nom == "Astérix").TomeMax);
        Assert.Equal(9, series.Single(s => s.Nom == "Thorgal").TomeMax);
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  Modification en lot
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Le_lot_pose_le_proprietaire_sans_toucher_aux_series()
    {
        var tintin = _referentiels.ResoudreSerie("Tintin");
        var asterix = _referentiels.ResoudreSerie("Astérix");
        var a = _livres.Creer(new Livre { Titre = "Le Lotus bleu", SerieId = tintin });
        var b = _livres.Creer(new Livre { Titre = "Astérix en Corse", SerieId = asterix });
        var temoin = _livres.Creer(new Livre { Titre = "Dune" });
        var lucas = _referentiels.ResoudreProprietaire("Lucas");

        Assert.Equal(2, _livres.ModifierEnLot([a, b], serieId: null, proprietaireId: lucas));

        Assert.Equal(("Tintin", "Lucas"), (_livres.ParId(a)!.SerieNom, _livres.ParId(a)!.ProprietaireNom));
        Assert.Equal(("Astérix", "Lucas"), (_livres.ParId(b)!.SerieNom, _livres.ParId(b)!.ProprietaireNom));
        Assert.Null(_livres.ParId(temoin)!.ProprietaireId);
    }

    [Fact]
    public void Le_lot_pose_la_serie_et_sait_retirer_le_proprietaire()
    {
        var lucas = _referentiels.ResoudreProprietaire("Lucas");
        var a = _livres.Creer(new Livre { Titre = "Astérix en Corse", ProprietaireId = lucas });
        var b = _livres.Creer(new Livre { Titre = "Astérix chez les Belges", ProprietaireId = lucas });

        _livres.ModifierEnLot([a, b], _referentiels.ResoudreSerie("Astérix"), Proprietaire.Aucun);

        Assert.All(new[] { a, b }.Select(id => _livres.ParId(id)!), l =>
        {
            Assert.Equal("Astérix", l.SerieNom);
            Assert.Null(l.ProprietaireId);
        });
    }

    [Fact]
    public void Le_lot_pose_genre_et_categorie_sans_toucher_au_reste()
    {
        var tintin = _referentiels.ResoudreSerie("Tintin");
        var a = _livres.Creer(new Livre
        {
            Titre = "Le Lotus bleu", SerieId = tintin,
            GenreId = _referentiels.ResoudreGenre("series:Tintin"),
        });
        var b = _livres.Creer(new Livre { Titre = "Astérix en Corse" });

        _livres.ModifierEnLot([a, b], serieId: null, proprietaireId: null,
            genreId: _referentiels.ResoudreGenre("Bande dessinée"),
            classementId: _referentiels.ResoudreClassement("BD des petits"));

        var lotus = _livres.ParId(a)!;
        Assert.Equal(("Bande dessinée", "BD des petits", "Tintin"),
            (lotus.GenreNom, lotus.ClassementNom, lotus.SerieNom));
        Assert.Equal("Bande dessinée", _livres.ParId(b)!.GenreNom);
    }

    [Fact]
    public void Un_lot_sans_rien_a_appliquer_n_ecrit_rien()
    {
        var a = _livres.Creer(new Livre { Titre = "Dune" });
        Assert.Equal(0, _livres.ModifierEnLot([a], null, null));
        Assert.Null(_livres.ParId(a)!.ModifieLe);
    }
}
