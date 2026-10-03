using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Exlibris.Convertisseurs;

/// <summary>
/// « Y a-t-il quelque chose ? » → Visible ou Collapsed.
///
/// Collapsed et non Hidden : Hidden rend invisible mais GARDE la place, ce qui
/// laisserait un blanc sous la vignette dépourvue de couverture.
///
/// Une chaîne d'espaces compte pour vide — c'est ce que rend un chemin non
/// renseigné, et l'invitation « cliquer pour agrandir » apparaîtrait alors sans
/// rien à agrandir. Proposer un clic sans effet apprend à ne plus essayer.
/// </summary>
public sealed class NonNulVersVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null                                       => Visibility.Collapsed,
            string texte when string.IsNullOrWhiteSpace(texte) => Visibility.Collapsed,
            _                                          => Visibility.Visible,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("NonNulVersVisibleConverter est à sens unique.");
}
