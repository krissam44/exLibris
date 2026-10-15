using System.Globalization;
using System.Text;

namespace Exlibris.Shared.Modeles;

/// <summary>
/// L'affichage en vignettes de la bibliothèque, comme la borne de Lirevia :
/// les couvertures en mosaïque, et une barre « Tout, A…Z, 0-9 » pour aller à
/// une lettre sans faire défiler cent couvertures.
/// </summary>
public static class Vignettes
{
    /// <summary>La lettre « 0-9 » de la barre : tout titre qui commence par un chiffre.</summary>
    public const char Chiffres = '#';

    /// <summary>Les lettres de la barre, dans l'ordre : A à Z, puis les chiffres.</summary>
    public static IReadOnlyList<char> Lettres { get; } = [.. Enumerable.Range('A', 26).Select(c => (char)c), Chiffres];

    /// <summary>
    /// La lettre sous laquelle un ouvrage se range : la première lettre de son
    /// titre complet (« Thorgal — 7. … » sous T), sans accent (« Éclats » sous
    /// E), et <see cref="Chiffres"/> pour « 1984 ». Nulle pour une fiche
    /// encore sans titre (scannée, pas enrichie) ou qui commence par un signe :
    /// elle n'apparaît que sous « Tout ».
    /// </summary>
    public static char? Initiale(Livre livre)
    {
        if (string.IsNullOrWhiteSpace(livre.Titre)) return null;
        foreach (var c in livre.TitreComplet.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsDigit(c)) return Chiffres;
            if (char.IsLetter(c))
            {
                // « Œuvres » sous O, « Æsope » sous A : la normalisation ne décompose pas les ligatures.
                var majuscule = char.ToUpperInvariant(c) switch { 'Œ' => 'O', 'Æ' => 'A', var m => m };
                return majuscule is >= 'A' and <= 'Z' ? majuscule : null;
            }
            // Guillemets, parenthèses, espaces en tête : on passe au caractère suivant.
        }
        return null;
    }

    /// <summary>
    /// Les ouvrages de la lettre choisie (tous si <paramref name="lettre"/> est
    /// nulle), dans l'ordre alphabétique du titre complet, sans tenir compte
    /// des accents ni de la casse — une série reste groupée, tome après tome
    /// tant que les numéros ont le même nombre de chiffres.
    /// </summary>
    public static List<Livre> Filtrer(IEnumerable<Livre> livres, char? lettre) =>
        livres.Where(l => lettre is null || Initiale(l) == lettre)
              .OrderBy(l => l.TitreComplet, StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace))
              .ToList();
}
