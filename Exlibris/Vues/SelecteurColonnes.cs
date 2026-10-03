using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Syncfusion.UI.Xaml.Grid;

namespace Exlibris.Vues;

/// <summary>
/// Une colonne de la grille, vue depuis la liste à cocher.
///
/// <b>Un intermédiaire plutôt qu'une liaison directe sur
/// <c>GridColumn.IsHidden</c>.</b> Cocher une case écrirait alors une valeur
/// LOCALE sur une propriété de dépendance, ce qui détruit la liaison qui s'y
/// trouvait — silencieusement, et définitivement pour la durée de la session.
/// La colonne « Propriétaire » en dépendait justement, et le réglage
/// correspondant aurait cessé de la commander dès la première visite du menu.
///
/// L'écriture passe donc par du code ordinaire, et un seul endroit décide de la
/// visibilité : <see cref="SelecteurColonnes"/>.
/// </summary>
public sealed partial class ColonneVisible(GridColumn colonne, Action apresChangement)
    : ObservableObject
{
    public string Cle => colonne.MappingName;
    public string Libelle => colonne.HeaderText;

    /// <summary>
    /// Empêche de décocher la DERNIÈRE colonne restante. Une grille sans aucune
    /// colonne n'affiche rien — pas même un message — et ne laisse aucun moyen
    /// visible de revenir en arrière : l'utilisateur croit avoir perdu ses
    /// livres.
    /// </summary>
    [ObservableProperty] private bool _verrouille;

    public bool Visible
    {
        get => !colonne.IsHidden;
        set
        {
            if (colonne.IsHidden == !value) return;

            colonne.IsHidden = !value;
            OnPropertyChanged();
            apresChangement();
        }
    }

    /// <summary>
    /// Relit l'état de la colonne, quand quelqu'un d'autre l'a changée
    /// (le réglage des propriétaires, ou la restauration au démarrage).
    /// </summary>
    public void Rafraichir() => OnPropertyChanged(nameof(Visible));
}

/// <summary>
/// Le sélecteur de colonnes d'une grille : quelles colonnes existent, lesquelles
/// sont montrées, et le souvenir de ce choix d'une session à l'autre.
///
/// <b>Pourquoi pas le <c>ColumnChooser</c> de Syncfusion.</b> Il existe, mais
/// c'est une fenêtre de GLISSER-DÉPOSER : on y traîne l'en-tête d'une colonne
/// pour la retirer, et on la ramène de la même façon. Personne ne devine ce
/// geste, et il exige d'activer le déplacement des colonnes — dont on ne veut
/// pas ici. Une liste à cocher se comprend sans mode d'emploi.
///
/// <b>Le choix est PERSISTANT.</b> Un sélecteur dont les décisions se perdent au
/// redémarrage se re-règle à chaque lancement, et cesse d'être utilisé au
/// troisième jour.
/// </summary>
public sealed class SelecteurColonnes
{
    private readonly SfDataGrid _grille;
    private readonly Func<string> _lire;
    private readonly Action<string> _ecrire;

    /// <summary>Séparateur des clés mémorisées. Aucun MappingName n'en contient.</summary>
    private const char Separateur = '|';

    public ObservableCollection<ColonneVisible> Colonnes { get; } = [];

    public SelecteurColonnes(SfDataGrid grille, Func<string> lire, Action<string> ecrire)
    {
        _grille = grille;
        _lire = lire;
        _ecrire = ecrire;
    }

    /// <summary>
    /// (Re)construit la liste, en appliquant d'abord les colonnes que
    /// l'application impose puis le choix mémorisé.
    ///
    /// <paramref name="imposeesMasquees"/> porte les colonnes qu'un réglage
    /// retire du jeu — « Propriétaire » quand la notion est désactivée. Elles
    /// sont masquées ET absentes de la liste : proposer de montrer une colonne
    /// toujours vide serait une invitation à un ennui.
    /// </summary>
    public void Construire(ISet<string> imposeesMasquees)
    {
        var memorisees = _lire()
            .Split(Separateur, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();

        Colonnes.Clear();

        foreach (var colonne in _grille.Columns)
        {
            if (imposeesMasquees.Contains(colonne.MappingName))
            {
                colonne.IsHidden = true;
                continue;
            }

            colonne.IsHidden = memorisees.Contains(colonne.MappingName);
            Colonnes.Add(new ColonneVisible(colonne, Enregistrer));
        }

        Verrouiller();
    }

    private void Enregistrer()
    {
        // Ce qu'on mémorise, ce sont les colonnes MASQUÉES, jamais les visibles.
        // Une colonne ajoutée dans une version ultérieure apparaîtra donc chez
        // les utilisateurs existants ; l'inverse l'aurait cachée à tout le monde
        // sauf aux nouvelles installations — un défaut qui ne se voit pas chez
        // le développeur, qui n'a pas de réglage enregistré.
        _ecrire(string.Join(Separateur,
            Colonnes.Where(c => !c.Visible).Select(c => c.Cle)));

        Verrouiller();
    }

    private void Verrouiller()
    {
        var visibles = Colonnes.Where(c => c.Visible).ToList();

        foreach (var colonne in Colonnes)
            colonne.Verrouille = visibles.Count == 1 && colonne.Visible;
    }
}
