using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Exlibris.Photino.Services;

/// <summary>
/// Rend les couvertures visibles dans la fenêtre.
///
/// <b>Le problème.</b> Les images sont dans le dossier des données
/// (<c>couvertures/</c>, voisin de la base), et la WebView ne peut pas lire un
/// fichier du disque : elle ne voit que ce que Photino lui sert, c'est-à-dire
/// <c>wwwroot</c>. Un <c>file:///</c> serait refusé, et convertir chaque image
/// en <c>data:</c> base64 ferait transiter des centaines de Kio par le canal
/// de rendu à chaque affichage.
///
/// <b>La solution.</b> Photino accepte sa propre source de fichiers : on lui
/// donne <c>wwwroot</c> PLUS le dossier des couvertures, publié sous le préfixe
/// <c>couvertures/</c>. Une image s'affiche alors par une simple adresse.
///
/// Pas de souci de cache : chaque image rangée par CouvertureDepot porte un
/// nom neuf (un GUID), si bien qu'une couverture remplacée change d'adresse —
/// le piège que l'ImageFichierConverter du WPF devait contourner n'existe pas.
/// </summary>
public sealed class Couvertures(string dossier)
{
    public const string Prefixe = "couvertures";

    public string Dossier { get; } = Path.GetFullPath(dossier);

    /// <summary>
    /// L'adresse d'une couverture à partir de son chemin complet (celui que
    /// donne CouvertureDepot.Chemin), ou <c>null</c> s'il n'y a rien à montrer.
    /// </summary>
    public string? Url(string? chemin)
    {
        if (string.IsNullOrWhiteSpace(chemin)) return null;

        var complet = Path.GetFullPath(chemin);
        // Seules les images du dossier sont servies : un chemin ailleurs ne
        // donnerait qu'une image cassée, autant le dire par « rien ».
        if (!string.Equals(Path.GetDirectoryName(complet), Dossier,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return null;

        return $"{Prefixe}/{Uri.EscapeDataString(Path.GetFileName(complet))}";
    }

    /// <summary>
    /// La source de fichiers à donner à Photino : wwwroot, plus les
    /// couvertures sous leur préfixe.
    /// </summary>
    public IFileProvider SourceDeFichiers(string wwwroot)
    {
        Directory.CreateDirectory(Dossier);
        return new CompositeFileProvider(
            new PhysicalFileProvider(wwwroot),
            new SousDossier(Prefixe, new PhysicalFileProvider(Dossier)));
    }

    /// <summary>
    /// Publie un dossier sous un préfixe d'adresse. Le PhysicalFileProvider
    /// intérieur refuse déjà toute remontée (« ../ ») hors de son dossier.
    /// </summary>
    private sealed class SousDossier(string prefixe, IFileProvider interieur) : IFileProvider
    {
        private readonly string _prefixe = prefixe.Trim('/') + "/";

        public IFileInfo GetFileInfo(string subpath)
        {
            var relatif = subpath.TrimStart('/');
            return relatif.StartsWith(_prefixe, StringComparison.Ordinal)
                ? interieur.GetFileInfo(relatif[_prefixe.Length..])
                : new NotFoundFileInfo(subpath);
        }

        // Jamais de liste du dossier : on sert une image par son nom, rien d'autre.
        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }
}
