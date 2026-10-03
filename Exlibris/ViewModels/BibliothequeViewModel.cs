using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Exlibris.Data;
using Exlibris.Services;
using Exlibris.Shared.Modeles;
using Exlibris.Vues;

namespace Exlibris.ViewModels;

/// <summary>
/// La liste des ouvrages : recherche, création, modification, suppression.
/// </summary>
public partial class BibliothequeViewModel(
    LivreDepot livres,
    ReferentielDepot referentiels,
    NoticeEnLigneService notices,
    CouvertureDepot couvertures,
    ParametreDepot parametres,
    IDialogueService dialogues) : ObservableObject
{
    public ObservableCollection<Livre> Livres { get; } = [];

    [ObservableProperty] private Livre? _selection;

    /// <summary>
    /// La saisie de recherche. Chaque frappe relance la requête — ce qui serait
    /// intenable sur les 540 000 fiches de Lirevia, et ne coûte rien ici : le
    /// fonds tient en quelques milliers de lignes, et la base est un fichier
    /// local.
    /// </summary>
    [ObservableProperty] private string _recherche = string.Empty;

    partial void OnRechercheChanged(string value) => Charger();

    /// <summary>Ce que la barre d'état affiche sous la grille.</summary>
    [ObservableProperty] private string _resume = string.Empty;

    /// <summary>
    /// Relu à CHAQUE chargement de l'écran, et non une fois pour toutes :
    /// l'utilisateur peut cocher l'option dans Paramètres puis revenir ici,
    /// et la colonne doit alors apparaître sans redémarrage.
    /// </summary>
    [ObservableProperty] private bool _proprietairesActifs;

    /// <summary>
    /// Les colonnes que l'utilisateur a décochées, retenues d'une session à
    /// l'autre.
    ///
    /// Le réglage vit ICI et non dans la vue parce qu'il se RANGE, et que la
    /// base est le seul endroit qui survive à un redémarrage. La vue sait ce
    /// qu'est une colonne ; le ViewModel ne manipule qu'une chaîne opaque, ce
    /// qui suffit à la conserver et lui évite de connaître Syncfusion.
    /// </summary>
    public string ColonnesMasquees
    {
        get => parametres.Lire(CleColonnesMasquees);
        set => parametres.Ecrire(CleColonnesMasquees, value);
    }

    private const string CleColonnesMasquees = "bibliotheque.colonnes.masquees";

    public void Charger()
    {
        ProprietairesActifs = parametres.LireBooleen(ParametreDepot.ProprietairesActifs);

        Livres.Clear();
        foreach (var livre in livres.Rechercher(Recherche))
            Livres.Add(livre);

        Resume = Livres.Count switch
        {
            0 when Recherche.Length > 0 => "Aucun ouvrage ne correspond.",
            0                           => "Votre bibliothèque est vide — commencez par un ouvrage.",
            1                           => "1 ouvrage",
            _                           => $"{Livres.Count} ouvrages",
        };
    }

    [RelayCommand]
    private void NouvelOuvrage()
    {
        var formulaire = new LivreFormulaireViewModel(
            referentiels, notices, couvertures, dialogues, ProprietairesActifs);

        if (Enregistrer(formulaire, "Nouvel ouvrage",
                "Seul le titre est obligatoire. Ce que vous ne savez pas se remplira plus tard, "
                + "à la main ou par la recherche d'ISBN."))
            Charger();
    }

    [RelayCommand]
    private void Modifier()
    {
        if (Selection is null) return;

        var formulaire = new LivreFormulaireViewModel(
            referentiels, notices, couvertures, dialogues, ProprietairesActifs, Selection);
        if (Enregistrer(formulaire, "Modifier l'ouvrage", string.Empty))
            Charger();
    }

    [RelayCommand]
    private void Supprimer()
    {
        if (Selection is null) return;

        // La confirmation est demandée parce que rien ne rattrape : ni
        // corbeille, ni historique. Le titre complet est cité pour qu'on voie
        // ce qu'on efface — « supprimer cet ouvrage ? » n'apprend rien quand on
        // a cliqué sur la mauvaise ligne.
        if (!dialogues.Confirmer(
                "Supprimer cet ouvrage ?",
                $"« {Selection.TitreComplet} » sera retiré de votre bibliothèque. "
                + "L'opération est définitive.",
                "Supprimer"))
            return;

        livres.Supprimer(Selection.Id);
        Charger();
    }

    /// <summary>
    /// Ouvre le dialogue, contrôle la saisie, enregistre. Renvoie <c>false</c>
    /// si l'utilisateur a renoncé.
    ///
    /// La boucle existe parce que <c>DialogueBase</c> ne connaît pas les règles
    /// du formulaire : il ferme sur validation, et c'est ici qu'on décide de le
    /// rouvrir tant que le titre manque. Le ViewModel n'étant pas reconstruit,
    /// l'utilisateur retrouve sa saisie intacte.
    /// </summary>
    private bool Enregistrer(LivreFormulaireViewModel formulaire, string titre, string aide)
    {
        while (true)
        {
            var vue = new LivreFormulaireVue { DataContext = formulaire };

            if (!dialogues.Saisir(titre, aide, vue)) return false;
            if (!formulaire.Valide()) continue;

            var livre = formulaire.VersLivre();

            long id;
            if (livre.Id == 0)
            {
                id = livres.Creer(livre);
            }
            else
            {
                livres.MettreAJour(livre);
                id = livre.Id;
            }

            livres.DefinirAuteurs(id, formulaire.AuteursResolus());
            return true;
        }
    }
}
