namespace Exlibris.Shared.Modeles;

/// <summary>
/// Empêche un même code-barres d'être traité en rafale.
///
/// <b>Le problème, constaté sur un téléphone réel.</b> Une caméra en détection
/// continue lit plusieurs fois par seconde : un livre qu'on présente une
/// seconde ou deux — le temps de le saisir, de le retourner, de le reposer —
/// remonte le même code cinq à vingt fois. Sans filtre, chaque passage produit
/// une ligne dans le journal, qui devient illisible au moment précis où l'on
/// compte dessus pour savoir où l'on en est.
///
/// <b>Pourquoi un silence et non un refus définitif.</b> Ignorer un code pour
/// toujours réglerait la rafale et casserait un geste légitime : on repasse
/// volontiers un livre devant la caméra, quelques minutes plus tard, pour
/// vérifier qu'il a bien été pris. Ce second passage doit répondre. Seule la
/// répétition IMMÉDIATE est du bruit.
///
/// L'instant est reçu en paramètre plutôt que lu ici : c'est ce qui permet
/// d'éprouver la règle sans attendre trois secondes par test.
/// </summary>
public class AntiRafale(TimeSpan? silence = null)
{
    /// <summary>
    /// Combien de temps un code reste ignoré après son passage.
    ///
    /// Trois secondes : plus long que la présentation d'un livre devant
    /// l'objectif, plus court que le temps d'en prendre un autre sur l'étagère.
    /// Trop court, la rafale repasse ; trop long, un vrai second livre au même
    /// code — deux exemplaires identiques — serait manqué.
    /// </summary>
    public static readonly TimeSpan SilenceParDefaut = TimeSpan.FromSeconds(3);

    private readonly TimeSpan _silence = silence ?? SilenceParDefaut;

    private readonly Dictionary<string, DateTime> _dernierPassage =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <c>true</c> si ce code doit être traité, <c>false</c> s'il n'est que
    /// l'écho du précédent.
    /// </summary>
    public bool Accepte(string? code, DateTime maintenant)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var propre = code.Trim();

        if (_dernierPassage.TryGetValue(propre, out var precedent) &&
            maintenant - precedent < _silence)
        {
            // ⚠️ On NE repousse PAS l'échéance ici. La repousser à chaque écho
            // ferait qu'un livre laissé devant l'objectif ne serait jamais
            // réaccepté, même longtemps après l'avoir retiré et représenté : le
            // silence se prolongerait indéfiniment de trois secondes en trois
            // secondes.
            return false;
        }

        _dernierPassage[propre] = maintenant;

        // La table ne peut pas enfler indéfiniment sur une séance de plusieurs
        // centaines d'ouvrages : ce qui est plus vieux que le silence ne sert
        // plus à rien.
        if (_dernierPassage.Count > 200) Oublier(maintenant);

        return true;
    }

    /// <summary>
    /// Efface la mémoire des passages.
    ///
    /// <b>Appelé quand l'utilisateur RÉCLAME une lecture</b> — l'appui sur
    /// « Scanner » du 04/09/2026. Le silence de trois secondes vaut contre les
    /// échos d'une caméra laissée en détection continue ; opposé à un geste
    /// délibéré, il ne serait qu'un refus muet. On repasse volontiers le livre
    /// qu'on vient de scanner, tout de suite, pour vérifier qu'il a bien été
    /// pris : cette lecture-là doit répondre, ne serait-ce que « déjà relevé ».
    /// </summary>
    public void Reinitialiser() => _dernierPassage.Clear();

    private void Oublier(DateTime maintenant)
    {
        foreach (var perime in _dernierPassage
                     .Where(p => maintenant - p.Value >= _silence)
                     .Select(p => p.Key)
                     .ToList())
            _dernierPassage.Remove(perime);
    }
}
