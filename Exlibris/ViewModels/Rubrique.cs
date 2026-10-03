using CommunityToolkit.Mvvm.ComponentModel;

namespace Exlibris.ViewModels;

/// <summary>
/// Une entrée du menu de gauche : son libellé, son icône, et le ViewModel de
/// l'écran qu'elle affiche.
///
/// <b>L'écran est porté par la rubrique, pas construit au clic.</b> Les
/// ViewModels sont créés une fois au démarrage et conservés : revenir sur la
/// Bibliothèque retrouve la recherche en cours et la ligne sélectionnée.
/// Reconstruire à chaque navigation rechargerait 7 000 livres pour rien et
/// perdrait le contexte de travail.
/// </summary>
public sealed partial class Rubrique(string libelle, string icone, ObservableObject vue) : ObservableObject
{
    public string Libelle { get; } = libelle;

    /// <summary>
    /// Caractère d'icône, pris dans « Segoe MDL2 Assets » — présente sur tout
    /// Windows 10 et 11. Aucune police à installer, aucune image à embarquer.
    /// </summary>
    public string Icone { get; } = icone;

    public ObservableObject Vue { get; } = vue;

    /// <summary>
    /// Entrée courante du menu.
    ///
    /// <b>Le menu REFLÈTE l'état, il ne le pilote pas.</b> C'est
    /// <see cref="MainViewModel.RubriqueActive"/> qui fait autorité et met ce
    /// drapeau à jour. Si le bouton radio était seul maître, une navigation
    /// déclenchée ailleurs — un lien depuis l'accueil, un retour après
    /// enregistrement — laisserait le menu marqué sur l'ancienne entrée, et
    /// l'utilisateur ne saurait plus où il est.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
}
