using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Exlibris.Convertisseurs;

/// <summary>
/// Charge une image depuis un chemin de fichier, <b>sans garder le fichier
/// ouvert</b>.
///
/// <b>C'est tout l'objet de cette classe.</b> Lier directement
/// <c>Image.Source</c> à un chemin fonctionne — et WPF garde alors le fichier
/// verrouillé aussi longtemps que l'image reste affichée. La couverture
/// deviendrait impossible à remplacer ou à supprimer tant que la fiche est
/// ouverte, et l'échec se présenterait comme un mystérieux « accès refusé ».
///
/// <c>BitmapCacheOption.OnLoad</c> lit tout en mémoire puis referme.
/// <c>IgnoreImageCache</c> traite le second cas : sans lui, WPF sert l'image
/// mémorisée pour un chemin déjà vu, si bien qu'une couverture remplacée
/// continuerait d'afficher l'ancienne jusqu'au redémarrage.
/// </summary>
public sealed class ImageFichierConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string chemin || string.IsNullOrWhiteSpace(chemin) || !File.Exists(chemin))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(chemin);
            image.EndInit();
            image.Freeze();   // utilisable depuis n'importe quel fil, et non modifiable

            return image;
        }
        catch (Exception e) when (e is NotSupportedException or IOException or UriFormatException)
        {
            // Fichier tronqué, format inattendu, disque indisponible : la fiche
            // s'affiche sans sa couverture. Une image illisible ne doit pas
            // empêcher de consulter le livre.
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("ImageFichierConverter est à sens unique.");
}
