using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// La reconnaissance d'un format d'image à ses premiers octets.
///
/// <b>Pourquoi ne pas se fier à l'extension ni au type MIME.</b> L'extension,
/// parce que Windows enregistre les JPEG du navigateur en <c>.jfif</c> — même
/// format, autre nom — et qu'un filtre sur « .jpg » les écarterait sans raison.
/// Le type MIME, parce qu'un serveur peut annoncer n'importe quoi : une page
/// d'erreur servie en <c>image/jpeg</c> serait enregistrée comme une couverture,
/// et l'écran montrerait un cadre vide sans rien pour l'expliquer.
/// </summary>
public class FormatImageTests
{
    /// <summary>
    /// Les signatures réelles, complétées de zéros. Ce sont les mêmes que
    /// reconnaît le décodeur de WPF, qui ignore lui aussi le nom du fichier.
    /// </summary>
    private static byte[] Debut(params byte[] signature) =>
        [.. signature, .. new byte[32]];

    [Fact]
    public void Un_fichier_JFIF_est_reconnu_comme_le_JPEG_qu_il_est()
    {
        // LE point de cette classe. Un « .jfif » enregistré depuis un navigateur
        // commence par la MÊME signature qu'un « .jpg » : la différence tient à
        // un segment d'en-tête interne, pas au format de l'image. Le reconnaître
        // aux octets le fait donc marcher sans qu'on ait eu à le prévoir.
        var jfif = Debut(0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46);

        Assert.Equal(".jpg", FormatImage.Extension(jfif));
        Assert.True(FormatImage.EstUneImage(jfif));
    }

    [Fact]
    public void Le_JPEG_ordinaire_est_reconnu_lui_aussi()
    {
        // Celui de la BnF : FF D8 FF E0, mesuré le 29/08/2026.
        Assert.Equal(".jpg", FormatImage.Extension(Debut(0xFF, 0xD8, 0xFF, 0xE0)));

        // Variante Exif des appareils photo — la photo d'un album pris au
        // téléphone, exactement le cas des 30 % sans code-barres.
        Assert.Equal(".jpg", FormatImage.Extension(Debut(0xFF, 0xD8, 0xFF, 0xE1)));
    }

    [Theory]
    [InlineData(".png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData(".gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]
    [InlineData(".bmp", new byte[] { 0x42, 0x4D, 0x36, 0x00 })]
    public void Les_autres_formats_courants_sont_reconnus(string attendu, byte[] signature) =>
        Assert.Equal(attendu, FormatImage.Extension(Debut(signature)));

    [Fact]
    public void Le_WebP_demande_de_regarder_au_dela_des_quatre_premiers_octets()
    {
        // « RIFF » ne suffit pas : un fichier audio WAV commence pareil. C'est
        // le marqueur en position 8 qui tranche, et le confondre ferait ranger
        // un son parmi les couvertures.
        byte[] webp = [.. "RIFF"u8, 0x20, 0, 0, 0, .. "WEBP"u8, .. new byte[16]];
        byte[] wav  = [.. "RIFF"u8, 0x20, 0, 0, 0, .. "WAVE"u8, .. new byte[16]];

        Assert.Equal(".webp", FormatImage.Extension(webp));
        Assert.Equal(string.Empty, FormatImage.Extension(wav));
    }

    [Fact]
    public void Une_page_HTML_d_erreur_n_est_PAS_une_image()
    {
        // Le cas qui compte vraiment : un serveur qui rend « <html>503 » avec un
        // en-tête image/jpeg. Se fier au type annoncé enregistrerait cette page
        // comme couverture, et l'on chercherait longtemps pourquoi le cadre
        // reste vide.
        Assert.False(FormatImage.EstUneImage("<html><body>503</body></html>"u8));
        Assert.Equal(string.Empty, FormatImage.Extension("<html>"u8));
    }

    [Fact]
    public void Un_contenu_trop_court_ne_fait_pas_tomber_la_reconnaissance()
    {
        // Une réponse tronquée, ou les 43 octets blancs d'Open Library : la
        // lecture des signatures ne doit pas déborder du tableau.
        Assert.Equal(string.Empty, FormatImage.Extension([]));
        Assert.Equal(string.Empty, FormatImage.Extension([0xFF]));
        Assert.Equal(string.Empty, FormatImage.Extension([0xFF, 0xD8]));

        // Trois octets : c'est exactement la longueur de la signature JPEG, donc
        // la limite où la comparaison doit encore réussir.
        Assert.Equal(".jpg", FormatImage.Extension([0xFF, 0xD8, 0xFF]));

        // « RIFF » sans les douze octets nécessaires au marqueur WEBP.
        Assert.Equal(string.Empty, FormatImage.Extension("RIFF"u8));
    }
}
