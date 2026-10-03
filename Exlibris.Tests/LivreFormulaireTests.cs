using Exlibris.Data;
using Exlibris.Services;
using Exlibris.Shared.Modeles;
using Exlibris.ViewModels;

namespace Exlibris.Tests;

/// <summary>
/// Le formulaire d'ouvrage, du point de vue de ce qu'il ÉCRIT en base.
///
/// <b>Le défaut qui a motivé ces tests (04/10/2026).</b> Le formulaire ne
/// connaît que les champs qu'il affiche ; <c>VersLivre</c> construisait un
/// livre neuf à partir d'eux seuls, et <c>LivreDepot.MettreAJour</c> écrit
/// TOUTES les colonnes. Modifier une fiche — corriger une faute dans le titre —
/// effaçait donc en silence le résumé, le nombre de pages, le format, la date
/// de lecture, la source de la notice et la date d'enrichissement. La fiche
/// retombait dans « À compléter », et le résumé rapporté par la BnF était
/// perdu sans que rien ne le signale.
/// </summary>
public class LivreFormulaireTests : IDisposable
{
    private readonly BancEssai _banc = new();
    private readonly LivreDepot _livres;
    private readonly ReferentielDepot _referentiels;
    private readonly HttpClient _client = new();

    public LivreFormulaireTests()
    {
        _livres = new LivreDepot(_banc.Fabrique);
        _referentiels = new ReferentielDepot(_banc.Fabrique);
    }

    public void Dispose()
    {
        _client.Dispose();
        _banc.Dispose();
    }

    /// <summary>Ouvre le formulaire sur un livre existant, comme le fait la Bibliothèque.</summary>
    private LivreFormulaireViewModel Ouvrir(Livre existant) => new(
        _referentiels,
        new NoticeEnLigneService(_client, string.Empty),
        new CouvertureDepot(_banc.Fabrique, _client),
        new DialoguesMuets(),
        proprietairesActifs: false,
        existant);

    [Fact]
    public void Modifier_une_fiche_conserve_les_champs_que_le_formulaire_n_affiche_pas()
    {
        var enrichiLe = new DateTime(2026, 9, 1, 10, 30, 0);
        var luLe = new DateTime(2026, 8, 15);

        var id = _livres.Creer(new Livre
        {
            Titre = "Germinal",
            Isbn = "9782253004226",
            NbPages = 600,
            Format = "Poche",
            Resume = "La grève des mineurs de Montsou.",
            DateLecture = luLe,
            SourceNotice = "BnF",
            EnrichiLe = enrichiLe,
        });

        // Le geste ordinaire : on ouvre la fiche, on corrige le titre, on enregistre.
        var formulaire = Ouvrir(_livres.ParId(id)!);
        formulaire.Titre = "Germinal (édition de poche)";
        _livres.MettreAJour(formulaire.VersLivre());

        var relu = _livres.ParId(id)!;

        Assert.Equal("Germinal (édition de poche)", relu.Titre);
        Assert.Equal(600, relu.NbPages);
        Assert.Equal("Poche", relu.Format);
        Assert.Equal("La grève des mineurs de Montsou.", relu.Resume);
        Assert.Equal(luLe, relu.DateLecture);
        Assert.Equal("BnF", relu.SourceNotice);
        Assert.Equal(enrichiLe, relu.EnrichiLe);
    }

    [Fact]
    public void Un_nouvel_ouvrage_part_sans_aucun_de_ces_champs()
    {
        // La contrepartie : un formulaire NEUF n'a rien à reporter. S'il
        // héritait de quoi que ce soit, ce serait d'un autre livre.
        var formulaire = new LivreFormulaireViewModel(
            _referentiels, new NoticeEnLigneService(_client, string.Empty),
            new CouvertureDepot(_banc.Fabrique, _client), new DialoguesMuets(),
            proprietairesActifs: false)
        {
            Titre = "Au bonheur des dames",
        };

        var livre = formulaire.VersLivre();

        Assert.Null(livre.NbPages);
        Assert.Equal(string.Empty, livre.Resume);
        Assert.Null(livre.EnrichiLe);
    }

    /// <summary>
    /// Aucun dialogue ne doit s'ouvrir dans ces tests : on n'y exerce que la
    /// construction du livre. Toute réponse est un refus.
    /// </summary>
    private sealed class DialoguesMuets : IDialogueService
    {
        public Task<bool> SaisirAsync(string titre, string aide, object formulaire, string libelleValider = "Enregistrer") => Task.FromResult(false);
        public Task<bool> ConfirmerAsync(string titre, string question, string libelleValider = "Confirmer") => Task.FromResult(false);
        public Task AfficherImageAsync(string titre, string chemin) => Task.CompletedTask;
        public Task<string?> ChoisirImageAsync() => Task.FromResult<string?>(null);
        public Task<string?> ChoisirFichierAsync(string titre, string filtre) => Task.FromResult<string?>(null);
    }
}
