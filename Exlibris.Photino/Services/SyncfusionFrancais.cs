using System.Resources;
using Syncfusion.Blazor;

namespace Exlibris.Photino.Services;

/// <summary>
/// Les textes des composants Syncfusion en français : filtres, pagination,
/// messages de grille. Sans lui, la grille parle anglais (« Clear Filter »,
/// « 1 of 2 pages ») au milieu d'une application française.
///
/// La traduction vient de Lirevia (Resources/SfResources.fr.resx), reprise ici
/// comme ressource PAR DÉFAUT et non comme satellite « fr » : Ex-libris n'existe
/// qu'en français, et une ressource neutre ne dépend ni de la culture du
/// système ni de la liste des langues embarquées à la publication.
/// </summary>
public sealed class SyncfusionFrancais : ISyncfusionStringLocalizer
{
    public ResourceManager ResourceManager { get; } =
        new("Exlibris.Photino.Resources.SfResources", typeof(SyncfusionFrancais).Assembly);

    // L'interface promet une chaîne non nulle, mais Syncfusion se rabat sur son
    // texte anglais quand on lui rend null — préférable à une clé affichée brute.
    public string GetText(string key) => ResourceManager.GetString(key)!;
}
