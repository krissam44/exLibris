using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.ViewModels;

/// <summary>
/// Les réglages, et l'entretien des référentiels.
///
/// Les deux cohabitent parce qu'ils relèvent du même geste : on vient ici de
/// temps en temps, pas tous les jours. Un écran « Référentiels » distinct
/// n'aurait porté qu'une grille, et le menu aurait gagné une entrée qu'on
/// n'ouvre que trois fois par an.
/// </summary>
public partial class ParametresViewModel(
    ParametreDepot parametres,
    ReferentielDepot referentiels,
    Services.IDialogueService dialogues) : ObservableObject
{
    // -----------------------------------------------------------------------------------------------------------------
    //  Réglages
    // -----------------------------------------------------------------------------------------------------------------

    [ObservableProperty] private bool _proprietairesActifs;

    /// <summary>
    /// Écrit dès que la case change, sans bouton « Enregistrer ».
    ///
    /// Un seul réglage ne justifie pas un bouton : il faudrait alors gérer
    /// l'abandon, l'avertissement en quittant l'écran, et le cas où l'on ferme
    /// l'application sans avoir validé. La case cochée EST la décision.
    /// </summary>
    partial void OnProprietairesActifsChanged(bool value) =>
        parametres.EcrireBooleen(ParametreDepot.ProprietairesActifs, value);

    // -----------------------------------------------------------------------------------------------------------------
    //  Propriétaires (demande du 12/11/2026)
    //
    //  Créés ICI, avant l'import : on choisit « Lucas » au moment de verser
    //  l'inventaire de ses BD, il doit donc déjà exister. La saisie libre de la
    //  fiche continue d'en créer à la volée, comme pour les autres listes.
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Les propriétaires, avec le nombre d'ouvrages de chacun.</summary>
    public ObservableCollection<EntreeReferentiel> ListeProprietaires { get; } = [];

    [ObservableProperty] private string _nouveauProprietaire = string.Empty;
    [ObservableProperty] private string _messageProprietaires = string.Empty;

    private void ChargerProprietaires()
    {
        ListeProprietaires.Clear();
        foreach (var p in referentiels.Lister("proprietaires"))
            ListeProprietaires.Add(p);

        // La grille des doublons montre peut-être les propriétaires : elle suit.
        if (ReferentielChoisi.Table == "proprietaires") ChargerEntrees();
    }

    [RelayCommand]
    private void AjouterProprietaire()
    {
        var nom = NouveauProprietaire.Trim();
        if (nom.Length == 0)
        {
            MessageProprietaires = "Saisissez un nom.";
            return;
        }

        var (_, cree) = referentiels.CreerProprietaire(nom);
        NouveauProprietaire = string.Empty;
        ChargerProprietaires();

        // « lucas » tapé alors que « Lucas » existe : on le dit, plutôt que
        // d'annoncer un ajout qui n'a pas eu lieu.
        MessageProprietaires = cree ? $"« {nom} » ajouté." : $"« {nom} » existait déjà.";
    }

    /// <summary>
    /// Renomme un propriétaire ; <c>false</c> si le nom est vide ou déjà pris
    /// par un autre — l'écran garde alors la saisie ouverte.
    /// </summary>
    public bool RenommerProprietaire(EntreeReferentiel proprietaire, string nom)
    {
        if (string.IsNullOrWhiteSpace(nom))
        {
            MessageProprietaires = "Le nom ne peut pas être vide.";
            return false;
        }

        if (!referentiels.RenommerProprietaire(proprietaire.Id, nom))
        {
            MessageProprietaires = $"« {nom.Trim()} » existe déjà. Pour réunir les deux, "
                                 + "passez par « Réunir les doublons », plus bas.";
            return false;
        }

        ChargerProprietaires();
        MessageProprietaires = $"« {proprietaire.Nom} » renommé en « {nom.Trim()} ».";
        return true;
    }

    /// <summary>
    /// Supprime un propriétaire après confirmation. Ses ouvrages restent, sans
    /// propriétaire : la question le dit, avec leur nombre.
    /// </summary>
    [RelayCommand]
    private async Task SupprimerProprietaireAsync(EntreeReferentiel proprietaire)
    {
        var question = proprietaire.NbOuvrages == 0
            ? $"« {proprietaire.Nom} » ne possède aucun ouvrage."
            : $"Ses {proprietaire.NbOuvrages} ouvrage{(proprietaire.NbOuvrages > 1 ? "s restent" : " reste")} dans la "
              + "bibliothèque, mais sans propriétaire.";

        if (!await dialogues.ConfirmerAsync($"Supprimer « {proprietaire.Nom} » ?", question, "Supprimer"))
            return;

        var liberes = referentiels.SupprimerProprietaire(proprietaire.Id);
        ChargerProprietaires();
        MessageProprietaires = liberes == 0
            ? $"« {proprietaire.Nom} » supprimé."
            : $"« {proprietaire.Nom} » supprimé — {liberes} ouvrage{(liberes > 1 ? "s" : "")} sans propriétaire.";
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  Référentiels
    // -----------------------------------------------------------------------------------------------------------------

    public IReadOnlyList<Referentiel> Referentiels { get; } = Referentiel.Tous;

    /// <summary>Celui qu'on inspecte. Les éditeurs d'abord : ce sont eux qui se dédoublent.</summary>
    [ObservableProperty] private Referentiel _referentielChoisi = Referentiel.Tous[0];

    public ObservableCollection<EntreeReferentiel> Entrees { get; } = [];

    /// <summary>
    /// Ce que l'utilisateur a coché — lu sur les entrées elles-mêmes.
    ///
    /// ⚠️ Et non sur <c>SfDataGrid.SelectedItems</c>, à quoi cet écran se fiait
    /// jusqu'au 30/08/2026 : trois lignes en surbrillance, et « Réunir »
    /// répondait « sélectionnez au moins deux entrées ». Voir
    /// <see cref="EntreeReferentiel.Choisi"/>.
    /// </summary>
    private List<EntreeReferentiel> Cochees => Entrees.Where(e => e.Choisi).ToList();

    [ObservableProperty] private string _messageFusion = string.Empty;

    partial void OnReferentielChoisiChanged(Referentiel value) => ChargerEntrees();

    public void Charger()
    {
        ProprietairesActifs = parametres.LireBooleen(ParametreDepot.ProprietairesActifs);
        MessageProprietaires = string.Empty;
        ChargerProprietaires();
        ChargerEntrees();
    }

    private void ChargerEntrees()
    {
        // Les abonnements de la liste précédente sont retirés : sans cela,
        // chaque changement de référentiel laisserait derrière lui une entrée
        // qui continue de faire recompter le message. Invisible sur trois
        // allers-retours, pesant sur trois cents.
        foreach (var ancienne in Entrees)
            ancienne.PropertyChanged -= SurCaseCochee;

        Entrees.Clear();

        foreach (var entree in referentiels.Lister(ReferentielChoisi.Table))
        {
            entree.PropertyChanged += SurCaseCochee;
            Entrees.Add(entree);
        }

        AnnoncerEtat();
    }

    /// <summary>
    /// Le message suit les cases EN DIRECT.
    ///
    /// C'est la réponse au vrai défaut de l'écran précédent : on ne savait pas
    /// ce que l'application avait compris de nos clics avant d'appuyer sur
    /// « Réunir ». Voir le compte monter à chaque case cochée dit tout de suite
    /// si le geste a porté.
    /// </summary>
    private void SurCaseCochee(object? source, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntreeReferentiel.Choisi)) AnnoncerEtat();
    }

    private void AnnoncerEtat()
    {
        var cochees = Cochees.Count;

        MessageFusion =
            Entrees.Count == 0 ? "Rien à entretenir ici pour l'instant."
            : cochees == 0 ? $"{Entrees.Count} entrée{(Entrees.Count > 1 ? "s" : "")} — "
                           + "cochez celles à réunir."
            : cochees == 1 ? "1 entrée cochée — il en faut au moins deux."
            : $"{cochees} entrées cochées.";
    }

    /// <summary>
    /// Verse plusieurs libellés dans un seul.
    ///
    /// <b>Le survivant est celui qui porte le plus d'ouvrages</b>, choisi ici et
    /// non demandé. C'est presque toujours le bon — devant quatre « Livre de
    /// poche », on garde celui qui en porte deux cents — et la confirmation le
    /// nomme explicitement, donc rien ne se décide dans le dos de personne. À
    /// égalité, le plus ancien l'emporte : au moins le choix est stable d'une
    /// exécution à l'autre.
    /// </summary>
    [RelayCommand]
    private async Task FusionnerAsync()
    {
        var choisies = Cochees;

        if (choisies.Count < 2)
        {
            MessageFusion = "Cochez au moins deux entrées à réunir.";
            return;
        }

        var survivant = choisies
            .OrderByDescending(e => e.NbOuvrages)
            .ThenBy(e => e.Id)
            .First();

        var absorbes = choisies.Where(e => e.Id != survivant.Id).ToList();

        // La confirmation NOMME le survivant et les absorbés. « Réunir ces
        // 4 entrées ? » laisserait ignorer laquelle survit, et l'opération est
        // sans retour : ni corbeille, ni historique.
        var detail = string.Join("\n", absorbes.Select(e =>
            $"• « {e.Nom} » ({e.NbOuvrages} ouvrage{(e.NbOuvrages > 1 ? "s" : "")})"));

        if (!await dialogues.ConfirmerAsync(
                "Réunir ces entrées ?",
                $"« {survivant.Nom} » sera conservé.\n\n{detail}\n\n"
                + "Leurs ouvrages lui seront rattachés, et ces libellés ne reviendront "
                + "plus : une notice qui les emploierait retombera désormais sur celui "
                + "que vous gardez.",
                "Réunir"))
            return;

        var deplaces = referentiels.Fusionner(
            ReferentielChoisi.Table, survivant.Id, absorbes.Select(e => e.Id));

        ChargerEntrees();

        MessageFusion = $"{absorbes.Count} entrée{(absorbes.Count > 1 ? "s réunies" : " réunie")} "
                      + $"dans « {survivant.Nom} » — {deplaces} ouvrage{(deplaces > 1 ? "s déplacés" : " déplacé")}.";
    }
}
