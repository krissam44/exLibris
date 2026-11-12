using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace Exlibris.Photino.Composants;

/// <summary>
/// Le filet sous les écrans : une exception d'un écran (rendu ou clic) est
/// ÉCRITE dans erreurs.log et MONTRÉE, au lieu de figer la fenêtre en silence.
///
/// Sans lui (constaté le 12/11/2026), une exception dans un gestionnaire de
/// clic partait dans la seule console de la WebView, que personne ne voit : le
/// bouton ne faisait simplement rien, et aucun journal n'en gardait trace.
/// </summary>
public sealed class GardeErreurs : ErrorBoundary
{
    [Inject] private ILogger<GardeErreurs> Journal { get; set; } = default!;

    protected override Task OnErrorAsync(Exception exception)
    {
        Journal.LogError(exception, "Erreur dans un écran");
        return Task.CompletedTask;
    }
}
