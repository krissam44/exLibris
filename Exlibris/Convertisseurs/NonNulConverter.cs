using System.Globalization;
using System.Windows.Data;

namespace Exlibris.Convertisseurs;

/// <summary>
/// « Y a-t-il quelque chose ? » — pour griser un bouton tant que rien n'est
/// sélectionné.
///
/// <b>Le bouton doit dire ce qui est possible</b>, plutôt que d'échouer après
/// le clic. Un « Modifier » toujours actif qui ne fait rien laisse croire à une
/// panne ; grisé, il apprend qu'il faut d'abord choisir une ligne.
/// </summary>
public sealed class NonNulConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    /// <summary>
    /// Sens inverse jamais employé : la liaison est en lecture seule. Lever
    /// plutôt que rendre une valeur arbitraire — si quelqu'un branche ce
    /// convertisseur en TwoWay un jour, il doit l'apprendre tout de suite et
    /// non découvrir une sélection silencieusement effacée.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("NonNulConverter est à sens unique.");
}
