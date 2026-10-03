using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Exlibris.Convertisseurs;

/// <summary>
/// Un booléen vers Visible / Collapsed.
///
/// Collapsed et non Hidden : le champ « Propriétaire » désactivé doit
/// disparaître, pas laisser un trou de trente pixels au milieu du formulaire —
/// ce qui donnerait à celui qui n'a pas activé l'option l'impression d'un
/// défaut d'affichage.
/// </summary>
public sealed class VraiVersVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("VraiVersVisibleConverter est à sens unique.");
}
