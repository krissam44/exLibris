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
    private void Fusionner()
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

        if (!dialogues.Confirmer(
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
