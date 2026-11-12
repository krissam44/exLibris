using CommunityToolkit.Mvvm.ComponentModel;
using Exlibris.Shared.Modeles;

namespace Exlibris.Photino.Formulaires;

/// <summary>
/// La correction en lot de la Bibliothèque (12/11/2026) : série, genre,
/// « Ma catégorie » et propriétaire pour tous les ouvrages cochés.
///
/// Propre à la version Photino, comme la consultation : le WPF n'a pas de
/// sélection multiple, et ses ViewModels n'ont pas à en porter la charge.
///
/// <b>Un champ laissé vide ne change rien.</b> On pose le propriétaire de
/// cinquante BD sans toucher à leurs séries, qui diffèrent d'un album à l'autre.
/// </summary>
public partial class ModificationEnLotViewModel(
    int nombre, IReadOnlyList<string> series, IReadOnlyList<string> genres, IReadOnlyList<string> classements,
    IReadOnlyList<Proprietaire> proprietaires, bool proprietairesActifs)
    : ObservableObject
{
    public int Nombre { get; } = nombre;
    public IReadOnlyList<string> Series { get; } = series;
    public IReadOnlyList<string> Genres { get; } = genres;
    public IReadOnlyList<string> Classements { get; } = classements;
    public IReadOnlyList<Proprietaire> Proprietaires { get; } = proprietaires;
    public bool ProprietairesActifs { get; } = proprietairesActifs;

    /// <summary>La série à poser ; vide = ne pas changer. Un nom nouveau crée la série.</summary>
    [ObservableProperty] private string _serie = string.Empty;

    /// <summary>Le genre à poser ; vide = ne pas changer. Un nom nouveau crée le genre.</summary>
    [ObservableProperty] private string _genre = string.Empty;

    /// <summary>
    /// « Ma catégorie » à poser ; vide = ne pas changer. Contrairement à la
    /// fiche, elle ne se recopie PAS du genre : sur cinquante ouvrages, une
    /// recopie implicite écraserait des catégories choisies une à une.
    /// </summary>
    [ObservableProperty] private string _classement = string.Empty;

    /// <summary>
    /// Le propriétaire à poser : <c>null</c> = ne pas changer,
    /// <see cref="Proprietaire.Aucun"/> = le retirer, sinon un identifiant.
    /// Liste FERMÉE : les propriétaires se créent dans Paramètres, une faute de
    /// frappe ici en ferait naître un sixième sur cinquante fiches.
    /// </summary>
    [ObservableProperty] private long? _proprietaire;

    [ObservableProperty] private string _erreur = string.Empty;

    partial void OnSerieChanged(string value) => Erreur = string.Empty;
    partial void OnGenreChanged(string value) => Erreur = string.Empty;
    partial void OnClassementChanged(string value) => Erreur = string.Empty;
    partial void OnProprietaireChanged(long? value) => Erreur = string.Empty;

    /// <summary>Rien à appliquer : le dialogue reste ouvert et le dit.</summary>
    public bool Valide()
    {
        var rien = string.IsNullOrWhiteSpace(Serie) && string.IsNullOrWhiteSpace(Genre)
                   && string.IsNullOrWhiteSpace(Classement) && Proprietaire is null;
        Erreur = rien ? "Remplissez au moins un champ à appliquer." : string.Empty;
        return Erreur.Length == 0;
    }
}
