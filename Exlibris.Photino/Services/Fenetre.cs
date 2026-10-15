using Photino.NET;

namespace Exlibris.Photino.Services;

/// <summary>
/// La fenêtre principale, pour les sélecteurs de fichiers. Posée par
/// <c>Program</c> juste après la construction de l'application : on ne dépend
/// pas de la façon dont PhotinoX expose, ou non, la fenêtre dans l'injection
/// de dépendances (repris de Budgevia).
/// </summary>
public sealed class Fenetre
{
    private PhotinoWindow? _principale;

    public PhotinoWindow Principale
    {
        get => _principale ?? throw new InvalidOperationException("La fenêtre principale n'est pas encore créée.");
        set => _principale = value;
    }
}
