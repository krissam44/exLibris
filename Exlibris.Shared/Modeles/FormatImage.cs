namespace Exlibris.Shared.Modeles;

/// <summary>
/// Reconnaît un format d'image à ses PREMIERS OCTETS, et non à ce qu'on nous en
/// dit.
///
/// <b>Ni l'extension ni le type MIME ne sont dignes de confiance.</b>
/// L'extension parce que Windows enregistre les JPEG du navigateur en
/// <c>.jfif</c> — même format, autre nom, et un filtre sur « .jpg » les écarte
/// sans raison. Le type MIME parce qu'un serveur peut annoncer
/// <c>application/octet-stream</c> pour une image parfaitement valide, ou
/// l'inverse : <c>image/jpeg</c> pour une page d'erreur.
///
/// Les octets, eux, ne mentent pas. C'est la même signature que reconnaît le
/// décodeur de WPF, qui se moque lui aussi du nom du fichier.
/// </summary>
public static class FormatImage
{
    /// <summary>
    /// L'extension à donner à ces octets, ou <b>chaîne vide</b> si ce n'est pas
    /// une image que nous sachions lire.
    ///
    /// La chaîne vide est le refus : elle évite d'enregistrer une page HTML
    /// d'erreur sous le nom d'une couverture, ce qui donnerait un cadre vide à
    /// l'écran sans rien pour l'expliquer.
    /// </summary>
    public static string Extension(ReadOnlySpan<byte> octets)
    {
        // JPEG — et donc JFIF, qui EST du JPEG : la différence tient à un
        // segment d'en-tête interne, pas au format de l'image. Un fichier
        // « .jfif » enregistré depuis un navigateur commence par ces mêmes trois
        // octets.
        if (Commence(octets, [0xFF, 0xD8, 0xFF])) return ".jpg";

        if (Commence(octets, [0x89, 0x50, 0x4E, 0x47])) return ".png";
        if (Commence(octets, "GIF8"u8)) return ".gif";
        if (Commence(octets, [0x42, 0x4D])) return ".bmp";

        // WebP : « RIFF », quatre octets de taille, puis « WEBP ».
        if (Commence(octets, "RIFF"u8) && octets.Length >= 12 &&
            octets.Slice(8, 4).SequenceEqual("WEBP"u8))
            return ".webp";

        return string.Empty;
    }

    /// <summary>Ces octets forment-ils une image que nous sachions lire ?</summary>
    public static bool EstUneImage(ReadOnlySpan<byte> octets) => Extension(octets).Length > 0;

    private static bool Commence(ReadOnlySpan<byte> octets, ReadOnlySpan<byte> signature) =>
        octets.Length >= signature.Length && octets[..signature.Length].SequenceEqual(signature);
}
