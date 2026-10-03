using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Exlibris.ViewModels;

namespace Exlibris.Vues;

public partial class BibliothequeVue : UserControl
{
    private SelecteurColonnes? _selecteur;

    public BibliothequeVue()
    {
        InitializeComponent();

        // DataContextChanged plutôt que le corps du constructeur : la vue est
        // instanciée par XAML et son ViewModel lui est posé APRÈS. Construire le
        // sélecteur ici lirait un DataContext nul, et la liste resterait vide
        // sans qu'aucune erreur ne soit levée.
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged ancien)
                ancien.PropertyChanged -= SurChangementViewModel;

            if (e.NewValue is not BibliothequeViewModel vm) return;

            vm.PropertyChanged += SurChangementViewModel;

            _selecteur = new SelecteurColonnes(
                Grille,
                () => vm.ColonnesMasquees,
                valeur => vm.ColonnesMasquees = valeur);

            ListeColonnes.ItemsSource = _selecteur.Colonnes;
            Reconstruire(vm);
        };
    }

    /// <summary>
    /// Le réglage des propriétaires est relu à chaque visite de l'écran (voir
    /// <c>BibliothequeViewModel.Charger</c>). La liste doit donc se reconstruire
    /// quand il change, sans quoi la colonne resterait masquée jusqu'au
    /// redémarrage alors que l'utilisateur vient de l'activer dans Paramètres.
    /// </summary>
    private void SurChangementViewModel(object? source, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BibliothequeViewModel.ProprietairesActifs)
            && source is BibliothequeViewModel vm)
            Reconstruire(vm);
    }

    private void Reconstruire(BibliothequeViewModel vm) =>
        _selecteur?.Construire(vm.ProprietairesActifs
            ? new HashSet<string>()
            : new HashSet<string> { "ProprietaireNom" });

    private void OuvrirColonnes(object sender, RoutedEventArgs e) =>
        PopupColonnes.IsOpen = true;

    /// <summary>
    /// Le double-clic ouvre la fiche de l'ouvrage — même geste que sur l'écran
    /// des Séries, et même raison : on enchaîne les corrections, et viser le
    /// bouton entre deux lignes coûte un aller-retour à chaque fois.
    ///
    /// C'est la COMMANDE qui est appelée, jamais un traitement parallèle : le
    /// bouton et le double-clic doivent faire exactement la même chose, sans
    /// quoi l'un des deux finira par oublier de recharger la liste.
    ///
    /// Le garde porte sur la sélection — un double-clic sous la dernière ligne,
    /// ou sur un en-tête de colonne, ne sélectionne rien.
    /// </summary>
    private void ModifierAuDoubleClic(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is BibliothequeViewModel { Selection: not null } vm)
            vm.ModifierCommand.Execute(null);
    }
}
