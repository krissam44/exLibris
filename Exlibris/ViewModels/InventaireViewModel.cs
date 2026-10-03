using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.ViewModels;

/// <summary>
/// Le versement d'un inventaire scanné, puis sa complétion.
///
/// <b>Deux temps, et l'écran les montre comme tels.</b> L'import est instantané
/// et ne demande rien au réseau ; la complétion dure — une demi-seconde par
/// ouvrage, donc plusieurs minutes pour une étagère. Les présenter ensemble,
/// derrière un seul bouton, ferait croire à un blocage au premier import un peu
/// gros.
/// </summary>
public partial class InventaireViewModel(
    InventaireDepot inventaire,
    Func<CompletionParLot> completion,
    Services.IDialogueService dialogues) : ObservableObject
{
    /// <summary>Les lignes du traitement en cours, la dernière en haut.</summary>
    public ObservableCollection<string> Journal { get; } = [];

    [ObservableProperty] private string _resume = string.Empty;
    [ObservableProperty] private int _enAttente;

    /// <summary>
    /// Vrai pendant la complétion. Grise les deux boutons — importer un second
    /// fichier pendant qu'un lot tourne mêlerait deux traitements sur la même
    /// base — et fait apparaître « Arrêter ».
    /// </summary>
    [ObservableProperty] private bool _enCours;

    private CancellationTokenSource? _annulation;

    public void Charger()
    {
        EnAttente = inventaire.NombreACompleter();

        if (Resume.Length == 0)
            Resume = EnAttente > 0
                ? $"{EnAttente} fiche{(EnAttente > 1 ? "s" : "")} attend{(EnAttente > 1 ? "ent" : "")} d'être complétée{(EnAttente > 1 ? "s" : "")}."
                : "Rien en attente.";
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  1. Verser le fichier
    // -----------------------------------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task ImporterAsync()
    {
        var chemin = await dialogues.ChoisirFichierAsync(
            "Choisir un inventaire",
            "Inventaire scanné (*.csv)|*.csv|Tous les fichiers|*.*");

        if (chemin is null) return;

        string contenu;
        try
        {
            // La signature UTF-8 est reconnue toute seule. Sans elle — un fichier
            // repassé par un éditeur peu soigneux — les accents des rangements
            // arriveraient abîmés, et « étagère » entrerait tel quel en base.
            contenu = File.ReadAllText(chemin);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Resume = "Ce fichier n'a pas pu être lu.";
            return;
        }

        var lignes = LigneInventaire.Analyser(contenu);

        if (lignes.Count == 0)
        {
            // Un fichier choisi par erreur, ou un partage interrompu. Le dire
            // franchement plutôt que d'annoncer « 0 ouvrage importé », qui
            // laisserait croire que le fichier était vide alors qu'il n'était
            // peut-être pas le bon.
            Resume = "Aucun ISBN lisible dans ce fichier — est-ce bien un inventaire ?";
            return;
        }

        var bilan = inventaire.Importer(lignes);
        EnAttente = inventaire.NombreACompleter();

        Journal.Clear();
        Resume = bilan.DejaPresents == 0
            ? $"{bilan.Crees} ouvrage{(bilan.Crees > 1 ? "s ajoutés" : " ajouté")}."
            : $"{bilan.Crees} ajouté{(bilan.Crees > 1 ? "s" : "")}, "
              + $"{bilan.DejaPresents} déjà présent{(bilan.DejaPresents > 1 ? "s" : "")} "
              + "— rien n'a été dupliqué.";
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  2. Compléter les fiches
    // -----------------------------------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task CompleterAsync()
    {
        if (EnCours || EnAttente == 0) return;

        EnCours = true;
        Journal.Clear();
        Resume = "Complétion en cours…";

        _annulation = new CancellationTokenSource();

        // `Progress<T>` capture le contexte de l'interface au moment de sa
        // construction : les lignes reviennent donc sur le fil graphique sans
        // que le service ait à en savoir quoi que ce soit. Écrire directement
        // dans la collection depuis la tâche de fond ferait lever WPF, et
        // seulement de temps en temps.
        // Typé en IProgress : « Report » est une implémentation EXPLICITE
        // d'interface sur Progress<T>, donc invisible depuis le type concret.
        IProgress<string> progres = new Progress<string>(ligne =>
        {
            Journal.Insert(0, ligne);

            // Le journal ne garde que ce qu'on peut lire. Trois cents lignes
            // conservées feraient enfler la mémoire pour un texte que personne
            // ne fera défiler.
            while (Journal.Count > 200) Journal.RemoveAt(Journal.Count - 1);
        });

        try
        {
            // Task.Run parce que le service est SYNCHRONE face à la base : sans
            // lui, chaque écriture figerait la fenêtre entre deux appels réseau,
            // et l'application paraîtrait plantée pendant toute la durée du lot.
            var bilan = await Task.Run(() => completion().TraiterAsync(
                taille: 500,
                progression: progres.Report,
                token: _annulation.Token));

            EnAttente = bilan.Restantes;

            Resume = bilan.Restantes > 0
                ? $"{bilan.Completees} complétée{(bilan.Completees > 1 ? "s" : "")}, "
                  + $"{bilan.Introuvables} introuvable{(bilan.Introuvables > 1 ? "s" : "")} — "
                  + $"{bilan.Restantes} en attente. Relancez pour continuer."
                : $"Terminé : {bilan.Completees} complétée{(bilan.Completees > 1 ? "s" : "")}, "
                  + $"{bilan.Introuvables} introuvable{(bilan.Introuvables > 1 ? "s" : "")}.";
        }
        finally
        {
            EnCours = false;
            _annulation.Dispose();
            _annulation = null;
        }
    }

    /// <summary>
    /// Interrompt le traitement. Ce qui a déjà été complété est acquis, et la
    /// fiche en cours retourne dans la file : relancer reprend exactement où
    /// l'on s'était arrêté.
    /// </summary>
    [RelayCommand]
    private void Arreter() => _annulation?.Cancel();
}
