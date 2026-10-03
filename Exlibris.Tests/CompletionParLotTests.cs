using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// La complétion en série des fiches importées.
///
/// <b>Aucun appel réseau</b> : la recherche et le téléchargement sont fournis au
/// service, qui reçoit ici des notices toutes prêtes. Ce qui est éprouvé est ce
/// qui peut vraiment mal tourner — un champ écrasé, une file qui n'avance pas,
/// un traitement qui s'arrête à la première source muette.
///
/// Le danger propre au traitement PAR LOT est qu'il répète sa faute : une règle
/// qui écrase un champ ne gâche pas une fiche, elle en gâche trois cents avant
/// qu'on ait le temps de le voir.
/// </summary>
public class CompletionParLotTests : IDisposable
{
    private readonly BancEssai _banc = new();
    private readonly InventaireDepot _inventaire;
    private readonly LivreDepot _livres;
    private readonly ReferentielDepot _referentiels;

    public CompletionParLotTests()
    {
        _inventaire = new InventaireDepot(_banc.Fabrique);
        _livres = new LivreDepot(_banc.Fabrique);
        _referentiels = new ReferentielDepot(_banc.Fabrique);
    }

    public void Dispose() => _banc.Dispose();

    /// <summary>Le service, alimenté par un dictionnaire d'ISBN connus.</summary>
    private CompletionParLot Service(
        Dictionary<string, NoticeEnLigne>? notices = null,
        string couverture = "image.jpg",
        List<string>? demandees = null) =>
        new(_inventaire, _livres, _referentiels,
            chercher: (isbn, _) =>
            {
                demandees?.Add(isbn);
                return Task.FromResult(notices is not null && notices.TryGetValue(isbn, out var n) ? n : null);
            },
            rapporterCouverture: (_, _) => Task.FromResult(couverture))
        { Pause = TimeSpan.Zero };   // la politesse envers les sources n'a pas lieu d'être ici

    private static NoticeEnLigne Notice(
        string titre, string auteurs = "", string editeur = "", string genre = "",
        int? annee = null, string couverture = "", string source = "BnF") =>
        new()
        {
            Titre = titre, Auteurs = auteurs, Editeur = editeur, Genre = genre,
            Annee = annee, CouvertureUrl = couverture, Source = source,
        };

    private long Importer(string isbn, string rangement = "Salon")
    {
        _inventaire.Importer([new LigneInventaire { Isbn = isbn, Rangement = rangement }]);
        return _livres.Rechercher().Single(l => l.Isbn == isbn).Id;
    }

    [Fact]
    public async Task Une_fiche_scannee_recoit_titre_auteur_editeur_et_annee()
    {
        var id = Importer("9782253106654");

        var bilan = await Service(new()
        {
            ["9782253106654"] = Notice("Quelqu'un d'autre", "Guillaume Musso",
                "Le Livre de poche", "Roman", 2026, "https://exemple/couv.jpg"),
        }).TraiterAsync();

        Assert.Equal(1, bilan.Completees);
        Assert.Equal(0, bilan.Restantes);

        var livre = _livres.ParId(id)!;
        Assert.Equal("Quelqu'un d'autre", livre.Titre);
        Assert.Equal("Guillaume Musso", livre.Auteurs);
        Assert.Equal("Le Livre de poche", livre.EditeurNom);
        Assert.Equal("Roman", livre.GenreNom);
        Assert.Equal(2026, livre.Annee);
        Assert.Equal("image.jpg", livre.Couverture);
        Assert.Equal("BnF", livre.SourceNotice);
    }

    [Fact]
    public async Task Le_rangement_releve_devant_l_etagere_est_PRESERVE()
    {
        // Le seul renseignement qu'aucune source ne donnera, et le seul qui
        // justifie d'être allé scanner sur place. L'écraser rendrait tout
        // l'inventaire inutile.
        var id = Importer("9782253106654", "Chambre de Paul, étagère du haut");

        await Service(new() { ["9782253106654"] = Notice("Quelqu'un d'autre") }).TraiterAsync();

        Assert.Equal("Chambre de Paul, étagère du haut", _livres.ParId(id)!.Rangement);
    }

    [Fact]
    public async Task Un_champ_deja_rempli_a_la_main_n_est_JAMAIS_ecrase()
    {
        // LE danger du traitement par lot : une règle fautive ne gâche pas une
        // fiche, elle en gâche trois cents avant qu'on ait le temps de le voir.
        var id = _livres.Creer(new Livre
        {
            Isbn = "9782253106654",
            Titre = "Quelqu'un d'autre (édition dédicacée)",
            Annee = 2025,
            EditeurId = _referentiels.ResoudreEditeur("Mon édition à moi"),
            Couverture = "ma-photo.jpg",
        });

        await Service(new()
        {
            ["9782253106654"] = Notice("Quelqu'un d'autre", editeur: "Le Livre de poche",
                annee: 2026, couverture: "https://exemple/couv.jpg"),
        }).TraiterAsync();

        var livre = _livres.ParId(id)!;
        Assert.Equal("Quelqu'un d'autre (édition dédicacée)", livre.Titre);
        Assert.Equal("Mon édition à moi", livre.EditeurNom);
        Assert.Equal(2025, livre.Annee);
        Assert.Equal("ma-photo.jpg", livre.Couverture);
    }

    [Fact]
    public async Task Des_auteurs_corriges_a_la_main_ne_sont_pas_remplaces()
    {
        // DefinirAuteurs fait table rase avant de réécrire : appelé sur une
        // fiche déjà renseignée, il remplacerait des rôles corrigés par des
        // « auteur » indéterminés.
        var id = _livres.Creer(new Livre { Isbn = "9782203001121", Titre = "Le Lotus bleu" });
        _livres.DefinirAuteurs(id, [(_referentiels.ResoudreAuteur("Hergé")!.Value, RoleAuteur.Indetermine)]);

        await Service(new()
        {
            ["9782203001121"] = Notice("Le Lotus bleu", auteurs: "Georges Remi, Quelqu'un d'autre"),
        }).TraiterAsync();

        Assert.Equal("Hergé", _livres.ParId(id)!.Auteurs);
    }

    [Fact]
    public async Task Un_ISBN_introuvable_sort_quand_meme_de_la_file()
    {
        // Sans cela, le lot suivant repartirait sur les mêmes introuvables et le
        // traitement n'avancerait jamais.
        Importer("9780000000000");

        var bilan = await Service(notices: []).TraiterAsync();

        Assert.Equal(1, bilan.Traitees);
        Assert.Equal(1, bilan.Introuvables);
        Assert.Equal(0, bilan.Restantes);
    }

    [Fact]
    public async Task Une_source_qui_leve_n_arrete_pas_les_suivantes()
    {
        // Trois cents ouvrages ne doivent pas s'arrêter sur un seul qui fâche.
        Importer("9782253106654");
        Importer("9782203001121");

        var service = new CompletionParLot(_inventaire, _livres, _referentiels,
            chercher: (isbn, _) => isbn == "9782253106654"
                ? throw new InvalidOperationException("source en panne")
                : Task.FromResult<NoticeEnLigne?>(Notice("Le Lotus bleu")),
            rapporterCouverture: (_, _) => Task.FromResult(string.Empty))
        { Pause = TimeSpan.Zero };

        var bilan = await service.TraiterAsync();

        Assert.Equal(2, bilan.Traitees);
        Assert.Equal(1, bilan.Completees);
        Assert.Equal(0, bilan.Restantes);
    }

    [Fact]
    public async Task Le_lot_est_borne_et_le_reste_annonce()
    {
        for (var i = 0; i < 5; i++) Importer($"978225310665{i}");

        var bilan = await Service(notices: []).TraiterAsync(taille: 2);

        Assert.Equal(2, bilan.Traitees);
        Assert.Equal(3, bilan.Restantes);
    }

    [Fact]
    public async Task Une_annulation_laisse_la_fiche_en_cours_dans_la_file()
    {
        // On ferme l'écran au milieu d'un traitement. La fiche interrompue n'a
        // pas eu sa chance : la marquer tentée la sortirait de la file sans
        // qu'aucune source ait répondu.
        Importer("9782253106654");
        Importer("9782203001121");

        using var jeton = new CancellationTokenSource();

        var service = new CompletionParLot(_inventaire, _livres, _referentiels,
            chercher: (_, _) => { jeton.Cancel(); throw new OperationCanceledException(); },
            rapporterCouverture: (_, _) => Task.FromResult(string.Empty))
        { Pause = TimeSpan.Zero };

        var bilan = await service.TraiterAsync(token: jeton.Token);

        Assert.Equal(0, bilan.Traitees);
        Assert.Equal(2, bilan.Restantes);
    }

    [Fact]
    public async Task Chaque_ouvrage_rend_une_ligne_de_progression()
    {
        // Le seul retour que l'utilisateur aura pendant plusieurs minutes.
        Importer("9782253106654");
        Importer("9780000000000");

        var lignes = new List<string>();
        await Service(new() { ["9782253106654"] = Notice("Quelqu'un d'autre") })
            .TraiterAsync(progression: lignes.Add);

        Assert.Equal(2, lignes.Count);
        Assert.Contains(lignes, l => l.StartsWith('✓') && l.Contains("Quelqu'un d'autre"));
        Assert.Contains(lignes, l => l.StartsWith('✕') && l.Contains("9780000000000"));
    }

    [Fact]
    public async Task Seules_les_fiches_en_attente_sont_interrogees()
    {
        // Un livre complet ne doit pas consommer un appel : les quotas des
        // sources gratuites se comptent en centaines par jour.
        Importer("9782253106654");

        var complet = _livres.Creer(new Livre { Isbn = "9782203001121", Titre = "Le Lotus bleu" });
        _inventaire.MarquerTentee(complet);

        var demandees = new List<string>();
        await Service(notices: [], demandees: demandees).TraiterAsync();

        Assert.Equal(["9782253106654"], demandees);
    }
}
