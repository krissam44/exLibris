using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// Le tableau de bord (12/11/2026), et d'abord les idées cadeaux : les tomes
/// manquants se comptent PAR PROPRIÉTAIRE — le tome que possède le fils manque
/// toujours au petit-fils.
/// </summary>
public class TableauDeBordTests : IDisposable
{
    private readonly BancEssai _banc = new();
    private readonly LivreDepot _livres;
    private readonly ReferentielDepot _referentiels;
    private readonly TableauDeBordDepot _tableau;

    public TableauDeBordTests()
    {
        _livres = new LivreDepot(_banc.Fabrique);
        _referentiels = new ReferentielDepot(_banc.Fabrique);
        _tableau = new TableauDeBordDepot(_banc.Fabrique);
    }

    public void Dispose() => _banc.Dispose();

    private void Tome(string serie, int numero, long? proprietaire, bool lu = false) =>
        _livres.Creer(new Livre
        {
            Titre = $"{serie} {numero}", SerieId = _referentiels.ResoudreSerie(serie),
            NumeroTome = numero, ProprietaireId = proprietaire, Lu = lu,
        });

    [Fact]
    public void Les_idees_cadeaux_se_calculent_sur_les_etageres_de_chacun()
    {
        var lucas = _referentiels.ResoudreProprietaire("Lucas")!.Value;
        var thomas = _referentiels.ResoudreProprietaire("Thomas")!.Value;
        Tome("Astérix", 1, lucas);
        Tome("Astérix", 3, lucas);
        Tome("Astérix", 2, thomas);   // celui-là, Lucas ne l'a pas

        var idee = Assert.Single(_tableau.IdeesCadeaux(lucas));
        Assert.Equal("Astérix", idee.Serie);
        Assert.Equal([2], idee.Manquants);

        // Thomas n'a que le tome 2 : il lui manque le 1.
        Assert.Equal([1], Assert.Single(_tableau.IdeesCadeaux(thomas)).Manquants);
    }

    [Fact]
    public void Le_nombre_de_tomes_de_la_serie_revele_la_fin_qui_manque()
    {
        var lucas = _referentiels.ResoudreProprietaire("Lucas")!.Value;
        Tome("Thorgal", 1, lucas);
        Tome("Thorgal", 2, lucas);
        _referentiels.MettreAJourSerie(_referentiels.ResoudreSerie("Thorgal")!.Value, 4, false, "");

        var idee = Assert.Single(_tableau.IdeesCadeaux(lucas));
        Assert.Equal([3, 4], idee.Manquants);
        Assert.Equal("3 et 4", idee.Resume);
    }

    [Fact]
    public void Une_serie_complete_ne_propose_rien_et_null_vise_toute_la_bibliotheque()
    {
        var lucas = _referentiels.ResoudreProprietaire("Lucas")!.Value;
        Tome("Tintin", 1, lucas);
        Tome("Tintin", 2, null);

        Assert.Empty(_tableau.IdeesCadeaux(lucas));                     // 1 seul tome, rien ne manque
        Assert.Empty(_tableau.IdeesCadeaux(proprietaireId: null));      // 1 et 2, au complet
        Assert.Equal([1], Assert.Single(_tableau.IdeesCadeaux(Proprietaire.Aucun)).Manquants);
    }

    [Fact]
    public void Chaque_proprietaire_a_sa_part_et_les_orphelins_leur_ligne()
    {
        var lucas = _referentiels.ResoudreProprietaire("Lucas")!.Value;
        _referentiels.ResoudreProprietaire("Thomas");   // créé, rien d'importé encore
        Tome("Astérix", 1, lucas, lu: true);
        Tome("Astérix", 2, lucas);
        Tome("Dune", 1, null);

        var parts = _tableau.ParProprietaire();

        Assert.Equal(
            [("Lucas", 2, 1), ("Thomas", 0, 0), ("Sans propriétaire", 1, 0)],
            parts.Select(p => (p.Nom, p.NbOuvrages, p.NbLus)));
        Assert.Equal(1, _tableau.NombreLus());
    }

    [Fact]
    public void Les_fiches_a_reprendre_ignorent_celles_qui_attendent_la_completion()
    {
        _livres.Creer(new Livre { Titre = "Dune" });                                   // rien d'autre
        _livres.Creer(new Livre { Titre = "Fondation", Couverture = "f.jpg", Resume = "Psychohistoire." });
        new InventaireDepot(_banc.Fabrique).Importer([new LigneInventaire { Isbn = "9782012101524" }]);

        var r = _tableau.AReprendre();

        Assert.Equal((1, 2, 2, 1), (r.SansCouverture, r.SansAuteur, r.SansGenre, r.SansResume));
    }

    [Fact]
    public void Une_bibliotheque_vide_rend_des_zeros()
    {
        var r = _tableau.AReprendre();
        Assert.Equal((0, 0, 0, 0), (r.SansCouverture, r.SansAuteur, r.SansGenre, r.SansResume));
        Assert.Empty(_tableau.ParProprietaire());
    }

    [Fact]
    public void Les_derniers_ajouts_viennent_du_plus_recent()
    {
        _livres.Creer(new Livre { Titre = "Premier" });
        _livres.Creer(new Livre { Titre = "Second" });

        Assert.Equal(["Second", "Premier"], _livres.DerniersAjouts(5).Select(l => l.Titre));
    }
}
