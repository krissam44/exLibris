using System.Windows.Controls;
using System.Windows.Input;
using Exlibris.ViewModels;

namespace Exlibris.Vues;

public partial class SeriesVue : UserControl
{
    public SeriesVue() => InitializeComponent();

    /// <summary>
    /// Le double-clic ouvre la fiche de la série sélectionnée.
    ///
    /// La COMMANDE est appelée telle quelle, et non un traitement parallèle :
    /// le double-clic et le bouton font ainsi exactement la même chose. Deux
    /// chemins vers la même action finissent toujours par diverger, et l'écart
    /// se découvre le jour où l'un des deux oublie de recharger la liste.
    ///
    /// Le garde porte sur la SÉLECTION et non sur <c>CanExecute</c> : la
    /// commande est un RelayCommand sans condition, qui répond donc toujours
    /// oui. Il couvre le double-clic dans le vide, sous la dernière ligne, où la
    /// grille ne sélectionne rien — le bouton, lui, se grise déjà tout seul.
    /// </summary>
    private void ModifierAuDoubleClic(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SeriesViewModel { Selection: not null } vm)
            vm.ModifierCommand.Execute(null);
    }
}
