using System.Globalization;
using System.Windows.Data;

namespace Exlibris.Convertisseurs;

/// <summary>
/// La négation d'un booléen — pour griser un bouton PENDANT que son action
/// tourne.
///
/// Le ViewModel expose « recherche en cours » plutôt que « bouton actif », et
/// c'est le bon sens de lecture : il décrit ce qu'il fait, pas l'état d'un
/// contrôle qu'il ne connaît pas. Sans ce convertisseur, il faudrait lui ajouter
/// une seconde propriété qui ne serait que l'inverse de la première — deux
/// vérités à tenir d'accord au lieu d'une.
/// </summary>
public sealed class NonVraiConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    /// <summary>
    /// Sens inverse jamais employé : la liaison est en lecture seule. Lever
    /// plutôt que rendre une valeur arbitraire, pour qu'un branchement en TwoWay
    /// se signale tout de suite.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("NonVraiConverter est à sens unique.");
}
