using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// Les propriétaires mis en œuvre (12/11/2026) : la gestion, le filtre de la
/// bibliothèque, l'import par propriétaire — et le résumé.
///
/// <b>Le besoin qui les motive</b> : savoir quelles BD possèdent le fils et le
/// petit-fils, pour ne pas leur offrir un album qu'ils ont déjà. D'où le point
/// le plus surveillé ici — le même ISBN chez deux propriétaires fait DEUX
/// fiches, sans quoi l'un des deux paraîtrait ne pas l'avoir.
/// </summary>
public class ProprietairesEtResumeTests : IDisposable
{
    private readonly BancEssai _banc = new();
    private readonly InventaireDepot _inventaire;
    private readonly LivreDepot _livres;
    private readonly ReferentielDepot _referentiels;

    public ProprietairesEtResumeTests()
    {
        _inventaire = new InventaireDepot(_banc.Fabrique);
        _livres = new LivreDepot(_banc.Fabrique);
        _referentiels = new ReferentielDepot(_banc.Fabrique);
    }

    public void Dispose() => _banc.Dispose();

    private const string AsterixEnCorse = "9782012101524";
    private const string LotusBleu = "9782203001121";

    private static LigneInventaire Ligne(string isbn) => new() { Isbn = isbn, Rangement = "Chambre" };

    // -----------------------------------------------------------------------------------------------------------------
    //  Import
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Le_meme_album_chez_deux_proprietaires_fait_deux_fiches()
    {
        var fils = _referentiels.ResoudreProprietaire("Thomas")!.Value;
        var petitFils = _referentiels.ResoudreProprietaire("Lucas")!.Value;

        _inventaire.Importer([Ligne(AsterixEnCorse)], fils);
        var bilan = _inventaire.Importer([Ligne(AsterixEnCorse), Ligne(LotusBleu)], petitFils);

        Assert.Equal(2, bilan.Crees);
        Assert.Equal(0, bilan.DejaPresents);
        Assert.Equal(["Lucas", "Thomas"],
            _livres.ParIsbn(AsterixEnCorse).Select(l => l.ProprietaireNom).Order());
    }

    [Fact]
    public void Reimporter_pour_le_meme_proprietaire_ne_duplique_rien()
    {
        var lucas = _referentiels.ResoudreProprietaire("Lucas")!.Value;

        _inventaire.Importer([Ligne(AsterixEnCorse)], lucas);
        var second = _inventaire.Importer([Ligne(AsterixEnCorse)], lucas);

        Assert.Equal(0, second.Crees);
        Assert.Equal(1, second.DejaPresents);
        Assert.Single(_livres.ParIsbn(AsterixEnCorse));
    }

    [Fact]
    public void Sans_proprietaire_l_import_se_compare_aux_fiches_sans_proprietaire()
    {
        // Le comportement d'avant, pour qui n'active pas la notion.
        _inventaire.Importer([Ligne(AsterixEnCorse)]);
        var second = _inventaire.Importer([Ligne(AsterixEnCorse)]);

        Assert.Equal(1, second.DejaPresents);
        Assert.Null(Assert.Single(_livres.ParIsbn(AsterixEnCorse)).ProprietaireId);
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  Filtre de la bibliothèque
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Le_filtre_isole_un_proprietaire_ou_les_ouvrages_sans_proprietaire()
    {
        var lucas = _referentiels.ResoudreProprietaire("Lucas")!.Value;
        var thomas = _referentiels.ResoudreProprietaire("Thomas")!.Value;
        _livres.Creer(new Livre { Titre = "Astérix en Corse", ProprietaireId = lucas });
        _livres.Creer(new Livre { Titre = "Le Lotus bleu", ProprietaireId = thomas });
        _livres.Creer(new Livre { Titre = "Dune" });

        Assert.Equal(3, _livres.Rechercher().Count);
        Assert.Equal("Astérix en Corse", Assert.Single(_livres.Rechercher(proprietaireId: lucas)).Titre);
        Assert.Equal("Dune", Assert.Single(_livres.Rechercher(proprietaireId: Proprietaire.Aucun)).Titre);
    }

    [Fact]
    public void Le_filtre_se_combine_avec_la_recherche()
    {
        var lucas = _referentiels.ResoudreProprietaire("Lucas")!.Value;
        var thomas = _referentiels.ResoudreProprietaire("Thomas")!.Value;
        _livres.Creer(new Livre { Titre = "Astérix en Corse", ProprietaireId = lucas });
        _livres.Creer(new Livre { Titre = "Astérix chez les Belges", ProprietaireId = thomas });

        // « Thomas a-t-il Astérix en Corse ? » — non.
        Assert.Empty(_livres.Rechercher("asterix en corse", thomas));
        Assert.Single(_livres.Rechercher("asterix", thomas));
    }

    [Fact]
    public void La_bibliotheque_rend_tous_ses_ouvrages_au_dela_de_500()
    {
        // L'ancienne borne : 500. Au-delà, un album possédé paraissait absent.
        using (var con = _banc.Fabrique.Ouvrir())
        using (var tx = con.BeginTransaction())
        {
            for (var i = 0; i < 600; i++)
                Dapper.SqlMapper.Execute(con,
                    "INSERT INTO livres (titre, cle_recherche) VALUES (@t, @t)", new { t = $"album {i}" }, tx);
            tx.Commit();
        }

        Assert.Equal(600, _livres.Rechercher().Count);
        Assert.Equal(600, _livres.Rechercher("album").Count);
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  Gestion
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Creer_un_proprietaire_existant_le_retrouve_sans_doublon()
    {
        var (id, cree) = _referentiels.CreerProprietaire("Lucas");
        var (encore, recree) = _referentiels.CreerProprietaire("lucas");

        Assert.True(cree);
        Assert.False(recree);
        Assert.Equal(id, encore);
        Assert.Single(_referentiels.Proprietaires());
    }

    [Fact]
    public void Renommer_garde_les_ouvrages_et_refuse_un_nom_deja_pris()
    {
        var lucas = _referentiels.ResoudreProprietaire("Lucas")!.Value;
        _referentiels.ResoudreProprietaire("Thomas");
        var livre = _livres.Creer(new Livre { Titre = "Astérix en Corse", ProprietaireId = lucas });

        Assert.False(_referentiels.RenommerProprietaire(lucas, "thomas"));
        Assert.True(_referentiels.RenommerProprietaire(lucas, "Lucas S."));

        Assert.Equal("Lucas S.", _livres.ParId(livre)!.ProprietaireNom);
    }

    [Fact]
    public void Supprimer_un_proprietaire_laisse_ses_ouvrages_sans_proprietaire()
    {
        var lucas = _referentiels.ResoudreProprietaire("Lucas")!.Value;
        var livre = _livres.Creer(new Livre { Titre = "Astérix en Corse", ProprietaireId = lucas });

        Assert.Equal(1, _referentiels.SupprimerProprietaire(lucas));

        Assert.Empty(_referentiels.Proprietaires());
        Assert.Null(_livres.ParId(livre)!.ProprietaireId);
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  Résumé
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Google_rend_son_resume_debarrasse_du_HTML()
    {
        var notice = NoticeEnLigneService.AnalyserGoogle("""
            {"items":[{"volumeInfo":{"title":"Astérix en Corse",
              "description":"<p>Les Gaulois partent en Corse.</p><p>Avec Ocatarinetabellatchitchix&nbsp;!</p>"}}]}
            """);

        Assert.Equal("Les Gaulois partent en Corse.\nAvec Ocatarinetabellatchitchix !", notice!.Resume);
    }

    [Fact]
    public void Lirevia_rend_son_resume()
    {
        var notice = NoticeEnLigneService.AnalyserLirevia(
            """{"titre":"Astérix en Corse","resume":"Les Gaulois partent en Corse.","source":"BnF"}""");

        Assert.Equal("Les Gaulois partent en Corse.", notice!.Resume);
    }

    [Fact]
    public async Task La_completion_par_lot_remplit_le_resume_seulement_s_il_est_vide()
    {
        _inventaire.Importer([Ligne(AsterixEnCorse), Ligne(LotusBleu)]);
        var lotus = _livres.ParIsbn(LotusBleu).Single();
        lotus.Resume = "Mon résumé à moi.";
        _livres.MettreAJour(lotus);

        var service = new CompletionParLot(_inventaire, _livres, _referentiels,
            chercher: (isbn, _) => Task.FromResult<NoticeEnLigne?>(new NoticeEnLigne
            {
                Titre = isbn, Resume = "Résumé de la source.", Source = "Google Books",
            }),
            rapporterCouverture: (_, _) => Task.FromResult(string.Empty))
        { Pause = TimeSpan.Zero };

        await service.TraiterAsync();

        Assert.Equal("Résumé de la source.", _livres.ParIsbn(AsterixEnCorse).Single().Resume);
        Assert.Equal("Mon résumé à moi.", _livres.ParIsbn(LotusBleu).Single().Resume);
    }
}
