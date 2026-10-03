using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Exlibris.Data;
using Exlibris.Services;
using Exlibris.Shared.Modeles;
using Exlibris.Vues;

namespace Exlibris.ViewModels;

/// <summary>
/// Les séries et leurs trous — l'écran qui justifie l'application, et qui
/// n'existe pas dans Lirevia.
/// </summary>
public partial class SeriesViewModel(
    ReferentielDepot referentiels,
    IDialogueService dialogues) : ObservableObject
{
    public ObservableCollection<Serie> Series { get; } = [];

    [ObservableProperty] private Serie? _selection;

    /// <summary>
    /// Filtre actif PAR DÉFAUT, et c'est délibéré : on ne vient pas ici admirer
    /// les séries complètes. Sur un fonds de plusieurs centaines de titres, la
    /// liste intégrale noierait les quinze lignes qui demandent une action.
    ///
    /// ⚠️ Il retient les séries « à traiter » et non les seules « incomplètes »
    /// — voir <see cref="Serie.ATraiter"/>. Une série dont on ignore le nombre
    /// de tomes ne manque de rien par construction : filtrée sur l'incomplétude
    /// seule, elle disparaissait de l'écran le jour même de sa création.
    /// </summary>
    [ObservableProperty] private bool _incompletesSeulement = true;

    partial void OnIncompletesSeulementChanged(bool value) => Charger();

    [ObservableProperty] private string _resume = string.Empty;

    public void Charger()
    {
        // L'identifiant est retenu AVANT le vidage. Les objets sont reconstruits
        // à chaque lecture : comparer les références rendrait toujours « rien de
        // sélectionné », et l'utilisateur qui renseigne dix séries d'affilée
        // repartirait du haut de la liste après chaque enregistrement.
        var selectionne = Selection?.Id;

        Series.Clear();

        var toutes = referentiels.Series();
        foreach (var serie in toutes.Where(s => !IncompletesSeulement || s.ATraiter))
            Series.Add(serie);

        Selection = Series.FirstOrDefault(s => s.Id == selectionne);

        var manquants = toutes.Sum(s => s.Manquants.Count);
        var masquees = toutes.Count - Series.Count;

        Resume = toutes.Count switch
        {
            0 => "Aucune série pour l'instant — elles naissent des ouvrages que vous saisissez.",
            _ when Series.Count == 0 => $"{toutes.Count} séries, aucune n'appelle d'action.",

            // Le nombre de séries MASQUÉES est dit, et ce n'est pas un détail :
            // sans lui, une série absente de la liste est indiscernable d'une
            // série oubliée par l'application. C'est exactement ce qui s'est
            // passé le 30/08/2026 avec Thorgal.
            _ => $"{Series.Count} série{(Series.Count > 1 ? "s" : "")} à traiter, "
               + $"{manquants} tome{(manquants > 1 ? "s" : "")} manquant{(manquants > 1 ? "s" : "")}"
               + (masquees > 0
                    ? $" — {masquees} autre{(masquees > 1 ? "s" : "")} série{(masquees > 1 ? "s" : "")} "
                      + "sans rien à signaler."
                    : "."),
        };
    }

    /// <summary>
    /// Renseigne ce qu'aucune source publique ne donne : le nombre de tomes de
    /// la série, et si elle est terminée.
    /// </summary>
    [RelayCommand]
    private void Modifier()
    {
        if (Selection is null) return;

        var formulaire = new SerieFormulaireViewModel(Selection);
        var vue = new SerieFormulaireVue { DataContext = formulaire };

        if (!dialogues.Saisir(
                $"Série « {Selection.Nom} »",
                "Le nombre de tomes ne figure dans aucune base publique de façon fiable : il se "
                + "saisit à la main, et c'est lui qui révèle les tomes manquants À LA FIN d'une "
                + "série. Sans lui, seuls les trous intérieurs se détectent.",
                vue))
            return;

        referentiels.MettreAJourSerie(
            Selection.Id, formulaire.TomesAnnoncesSaisis, formulaire.Terminee, formulaire.Note);

        Charger();
    }
}

/// <summary>
/// Les trois renseignements qu'un humain seul peut donner sur une série.
///
/// Le nom n'y figure qu'en lecture : le renommage passe par la fusion des
/// libellés, qui sait rapprocher « Thorgal » et « Thorgal (Le Lombard) » sans
/// laisser d'ouvrage orphelin.
/// </summary>
public partial class SerieFormulaireViewModel : ObservableObject
{
    public SerieFormulaireViewModel(Serie serie)
    {
        Nom = serie.Nom;
        Possedes = serie.NbPossedes;
        ResumeManquants = serie.ResumeManquants;

        // Le champ est du TEXTE, et non un entier nullable lié directement : un
        // champ numérique vidé rend 0 dans la plupart des liaisons WPF, ce qui
        // écraserait « je ne sais pas » par « série de zéro tome » dès la
        // première ouverture du dialogue sur une série non renseignée.
        _tomesAnnonces = serie.TomesAnnonces?.ToString() ?? string.Empty;
        _terminee = serie.Terminee;
        _note = serie.Note;
    }

    public string Nom { get; }
    public int Possedes { get; }
    public string ResumeManquants { get; }

    [ObservableProperty] private string _tomesAnnonces;
    [ObservableProperty] private bool _terminee;
    [ObservableProperty] private string _note;

    /// <summary>
    /// La saisie relue en nombre. Une frappe illisible vaut « je ne sais pas » :
    /// refuser le dialogue pour un caractère de trop coûterait plus cher que de
    /// laisser le champ vide, qui est de toute façon l'état de départ.
    /// </summary>
    public int? TomesAnnoncesSaisis =>
        int.TryParse(TomesAnnonces, out var n) && n > 0 ? n : null;
}
