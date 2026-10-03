using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;

namespace Exlibris.ViewModels;

/// <summary>
/// La coquille : le menu, la rubrique affichée, et l'identité de la
/// bibliothèque montrée dans la barre du haut.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    public MainViewModel(
        AccueilViewModel accueil,
        BibliothequeViewModel bibliotheque,
        SeriesViewModel series,
        InventaireViewModel inventaire,
        ParametresViewModel parametres)
    {
        Rubriques =
        [
            new Rubrique("Accueil",      "\uE80F", accueil),       // maison
            new Rubrique("Bibliothèque", "\uE8F1", bibliotheque),  // pile de livres
            new Rubrique("Séries",       "\uE8FD", series),        // liste
            new Rubrique("Inventaire",   "\uE8B7", inventaire),   // code-barres
            new Rubrique("Paramètres",   "\uE713", parametres),    // engrenage
        ];

        _rubriqueActive = Rubriques[0];
        // Posé à la main : l'affectation du CHAMP ci-dessus ne passe pas par la
        // propriété générée, donc OnRubriqueActiveChanged ne se déclenche pas.
        // Sans cette ligne, le menu s'ouvre sans entrée marquée alors que
        // l'accueil est bien affiché.
        _rubriqueActive.IsSelected = true;
        Rafraichir(_rubriqueActive.Vue);
    }

    public IReadOnlyList<Rubrique> Rubriques { get; }

    [ObservableProperty]
    private Rubrique _rubriqueActive;

    /// <summary>
    /// Menu réduit à ses icônes. Reprend le « mini drawer » de Lirevia : sur un
    /// écran de portable, 220 px de menu pris à la grille se remarquent.
    /// </summary>
    [ObservableProperty]
    private bool _menuReduit;

    /// <summary>Nom de la bibliothèque, au centre de la barre — comme dans Lirevia.</summary>
    public string NomBibliotheque => "Ma bibliothèque";

    /// <summary>
    /// La version, LUE dans l'assembly plutôt qu'écrite ici.
    ///
    /// Elle vient de <c>&lt;Version&gt;</c> du csproj, d'où l'installateur la
    /// tire aussi. Recopiée en dur, elle finissait par mentir : on incrémente
    /// le csproj pour publier, jamais la chaîne du pied de page — et le
    /// décalage ne se voit que le jour où l'on demande à quelqu'un « quelle
    /// version avez-vous ? ».
    ///
    /// <c>InformationalVersion</c> et non <c>Version</c> : la seconde est
    /// toujours complétée en quatre nombres (0.1.0.0), la première rend ce qui
    /// a été écrit (0.1.0). En revanche le SDK lui accole le hachage du commit
    /// quand le dépôt est sous Git — d'où la coupe au « + ».
    /// </summary>
    public string Version { get; } = Lire();

    private static string Lire()
    {
        var brute = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(brute)) return "Ex-libris";

        return $"Ex-libris — version {brute.Split('+')[0]}";
    }

    /// <summary>
    /// Tient à jour le drapeau de sélection du menu. Généré par
    /// CommunityToolkit à chaque changement de <see cref="RubriqueActive"/>,
    /// quelle qu'en soit l'origine — clic sur le menu ou navigation
    /// programmée.
    /// </summary>
    // Les noms des paramètres suivent ceux du générateur MVVM, sinon CS8826.
    partial void OnRubriqueActiveChanged(Rubrique? oldValue, Rubrique newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        newValue.IsSelected = true;

        // L'écran se rafraîchit À L'ARRIVÉE, et non à la construction : les
        // ViewModels étant conservés d'une visite à l'autre, l'accueil
        // afficherait sinon les chiffres du démarrage même après une saisie.
        Rafraichir(newValue.Vue);
    }

    /// <summary>
    /// Demande à un écran de se recharger, s'il sait le faire.
    ///
    /// Le test de type est laid mais honnête : l'alternative — une interface
    /// « IRafraichissable » — obligerait chaque écran à la déclarer, y compris
    /// ceux qui n'ont rien à recharger. Trois écrans ne justifient pas cette
    /// cérémonie ; le jour où il y en aura dix, l'interface se posera d'elle-même.
    /// </summary>
    private static void Rafraichir(ObservableObject vue)
    {
        switch (vue)
        {
            case AccueilViewModel accueil: accueil.Charger(); break;
            case BibliothequeViewModel bibliotheque: bibliotheque.Charger(); break;
            case SeriesViewModel series: series.Charger(); break;
            case InventaireViewModel inventaire: inventaire.Charger(); break;
            case ParametresViewModel parametres: parametres.Charger(); break;
        }
    }

    [RelayCommand]
    private void BasculerMenu() => MenuReduit = !MenuReduit;

    [RelayCommand]
    private void Naviguer(Rubrique rubrique) => RubriqueActive = rubrique;
}
