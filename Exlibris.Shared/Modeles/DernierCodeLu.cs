namespace Exlibris.Shared.Modeles;

/// <summary>
/// Retient le dernier code signalé, pour le scan À LA VOLÉE.
///
/// <b>Pourquoi l'anti-rafale n'y suffit pas.</b> <see cref="AntiRafale"/> se tait
/// trois secondes puis réaccepte, et c'est voulu : un livre repassé plus tard
/// doit répondre. En mode « au toucher », la question ne se pose pas — un livre
/// reconnu désarme la caméra. En continu, rien ne la désarme : un livre laissé
/// devant l'objectif, ou l'étiquette de prix restée dans le champ, serait
/// resignalé toutes les trois secondes, vibration comprise, et le journal
/// redeviendrait illisible (demande du 11/09/2026).
///
/// La règle est donc : seul un code DIFFÉRENT du précédent mérite une ligne.
/// Revenir au premier après en avoir lu un autre le signale de nouveau — c'est
/// un geste, pas un écho.
/// </summary>
public class DernierCodeLu
{
    private string? _dernier;

    /// <summary>
    /// <c>true</c> si <paramref name="code"/> est celui qu'on vient de signaler.
    /// Sinon il devient le nouveau « dernier », et la méthode rend <c>false</c>.
    /// </summary>
    public bool EstLeMeme(string? code)
    {
        var propre = code?.Trim() ?? string.Empty;

        // Un code vide n'est jamais une répétition, et ne chasse pas le précédent.
        if (propre.Length == 0) return false;

        if (string.Equals(propre, _dernier, StringComparison.OrdinalIgnoreCase)) return true;

        _dernier = propre;
        return false;
    }

    /// <summary>
    /// Oublie le dernier code : après un appui délibéré ou une saisie à la main,
    /// le même livre doit de nouveau obtenir une réponse.
    /// </summary>
    public void Oublier() => _dernier = null;
}
