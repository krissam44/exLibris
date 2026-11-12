using CommunityToolkit.Mvvm.ComponentModel;
using Exlibris.Data;

namespace Exlibris.ViewModels;

/// <summary>
/// Tableau de bord : les quatre chiffres qu'on veut voir en ouvrant
/// l'application.
/// </summary>
public partial class AccueilViewModel(FabriqueConnexion fabrique) : ObservableObject
{
    [ObservableProperty] private int _nbLivres;
    [ObservableProperty] private int _nbSeries;
    [ObservableProperty] private int _nbSansIsbn;
    [ObservableProperty] private int _nbNonEnrichis;

    /// <summary>
    /// Une seule requête pour les quatre chiffres, plutôt que quatre allers-
    /// retours. Sur un fichier local la différence est mince, mais elle garantit
    /// surtout que les quatre nombres décrivent le MÊME instant.
    /// </summary>
    public void Charger()
    {
        using var con = fabrique.Ouvrir();

        var chiffres = Dapper.SqlMapper.QueryFirst<(int Livres, int Series, int SansIsbn, int NonEnrichis)>(
            con, """
            SELECT (SELECT COUNT(*) FROM livres),
                   (SELECT COUNT(*) FROM series),
                   (SELECT COUNT(*) FROM livres WHERE isbn IS NULL),
                   (SELECT COUNT(*) FROM livres WHERE enrichi_le IS NULL AND isbn IS NOT NULL)
            """);

        NbLivres      = chiffres.Livres;
        NbSeries      = chiffres.Series;
        NbSansIsbn    = chiffres.SansIsbn;
        NbNonEnrichis = chiffres.NonEnrichis;
    }
}
