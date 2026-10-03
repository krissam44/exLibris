namespace Exlibris.Shared.Modeles;

/// <summary>
/// Choisit, parmi les codes-barres lus sur UNE image, celui d'un livre.
///
/// <b>Une image en contient souvent plusieurs.</b> Sur une quatrième de
/// couverture voisinent l'EAN du livre, l'étiquette de prix du libraire et le
/// code de catégorie de l'éditeur ; sur un comptoir, s'ajoute le livre du
/// dessous. La caméra les remonte ensemble, dans un ordre qui n'a aucun sens
/// pour nous — c'est celui de la détection, pas de l'importance.
///
/// <b>Ne prendre que le premier était le défaut.</b> L'application le
/// confrontait à l'ISBN, le rejetait, et l'ouvrage repartait non lu alors que
/// son code figurait dans la même image, deux rangs plus loin. Constaté en
/// librairie le 31/08/2026 : beaucoup de non-lectures, sur des codes-barres
/// parfaitement nets.
/// </summary>
public static class ChoixCodeBarres
{
    /// <summary>
    /// Le premier code de la liste qui soit un livre, ou chaîne vide.
    ///
    /// Retourne la forme NORMALISÉE : c'est elle qui entre dans le relevé, et
    /// la comparer à ce qu'a affiché l'écran doit rester possible.
    /// </summary>
    public static string PremierLivre(IEnumerable<string?>? codes)
    {
        if (codes is null) return string.Empty;

        foreach (var code in codes)
        {
            var propre = Isbn.Normaliser(code);
            if (Isbn.EstLivre(propre)) return propre;
        }

        return string.Empty;
    }
}
