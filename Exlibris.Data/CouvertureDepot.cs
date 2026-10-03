using Exlibris.Shared.Modeles;

namespace Exlibris.Data;

/// <summary>
/// Range les couvertures sur le disque, à côté de la base.
///
/// <b>Le fichier plutôt que l'URL</b>, et c'est la différence de fond avec
/// Lirevia. Là-bas, une page web affiche l'image directement depuis la source :
/// le navigateur la charge, la met en cache, et rien n'est à stocker. Ici
/// l'application est de bureau et le fonds est le vôtre — une bibliothèque qui
/// n'afficherait plus ses couvertures parce que Google a changé une adresse, ou
/// parce que vous cataloguez dans le train, serait une bibliothèque diminuée.
/// Le schéma l'avait prévu dès l'origine : <c>livres.couverture</c> porte un
/// chemin RELATIF au dossier <c>couvertures/</c> voisin du fichier <c>.db</c>.
///
/// ⚠️ <b>Sauvegarder, c'est donc copier DEUX choses</b> : la base et ce dossier.
/// C'est le prix de ce choix, et il est assumé — l'inverse (des images dans la
/// base) ferait grossir le <c>.db</c> de plusieurs centaines de méga-octets sur
/// 7 000 albums, alors qu'un fichier se remplace sans y toucher.
/// </summary>
public class CouvertureDepot(FabriqueConnexion fabrique, HttpClient client)
{
    /// <summary>
    /// En dessous de quoi une réponse n'est pas une couverture.
    ///
    /// <b>Mesuré, pas deviné</b> : le 29/08/2026, covers.openlibrary.org a
    /// répondu <b>200 avec 43 octets</b> pour un ISBN qu'il ne connaît pas — une
    /// image blanche, servie comme si de rien n'était. Sans ce seuil, on
    /// enregistrerait un fichier valide, on afficherait un rectangle vide, et on
    /// chercherait longtemps pourquoi la couverture « ne s'affiche pas ».
    ///
    /// 4 Kio laisse passer la plus modeste des vraies vignettes (celle de
    /// Google fait quelques dizaines de Kio) et arrête tous les substituts.
    /// </summary>
    private const int TailleMinimale = 4 * 1024;

    /// <summary>Le dossier des images, voisin du fichier de base.</summary>
    public string Dossier { get; } = Path.Combine(
        Path.GetDirectoryName(fabrique.CheminBase) ?? ".", "couvertures");

    /// <summary>
    /// Télécharge l'image et renvoie son NOM DE FICHIER, à ranger dans
    /// <c>livres.couverture</c>. Chaîne vide si rien n'a pu être récupéré.
    ///
    /// Ne lève jamais : une couverture manquante est un désagrément, pas une
    /// raison d'interrompre un catalogage.
    /// </summary>
    public async Task<string> TelechargerAsync(string? url, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        try
        {
            using var reponse = await client.GetAsync(url, token);
            if (!reponse.IsSuccessStatusCode) return string.Empty;

            var octets = await reponse.Content.ReadAsByteArrayAsync(token);

            return await RangerAsync(octets, token);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Le chemin complet d'une couverture, ou <c>null</c> si elle manque.
    ///
    /// Le <c>null</c> couvre deux cas qu'il ne sert à rien de distinguer à
    /// l'écran : aucune couverture enregistrée, et un fichier disparu — dossier
    /// non restauré depuis une sauvegarde, par exemple. Dans les deux cas
    /// l'écran montre la même absence, et l'application continue.
    /// </summary>
    public string? Chemin(string? nomFichier)
    {
        if (string.IsNullOrWhiteSpace(nomFichier)) return null;

        var complet = Path.Combine(Dossier, nomFichier);
        return File.Exists(complet) ? complet : null;
    }

    /// <summary>
    /// Reprend une image DÉJÀ sur le disque — une photo de l'album, une
    /// couverture enregistrée depuis un navigateur — et la range comme les
    /// autres.
    ///
    /// Indispensable pour les ~30 % d'albums sans code-barres : aucune source en
    /// ligne ne les connaît, donc aucune ne rendra leur couverture. Le fichier
    /// est COPIÉ, jamais référencé sur place : l'original peut être dans un
    /// dossier de téléchargements que l'on vide, ou sur une clé qu'on retire.
    /// </summary>
    public async Task<string> ImporterAsync(string? cheminSource, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(cheminSource) || !File.Exists(cheminSource))
            return string.Empty;

        try
        {
            return await RangerAsync(await File.ReadAllBytesAsync(cheminSource, token), token);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Le seul endroit qui écrit dans le dossier — que l'image vienne du réseau
    /// ou du disque.
    ///
    /// <b>Le format est reconnu aux OCTETS</b>, jamais au type annoncé ni à
    /// l'extension d'origine. C'est ce qui fait qu'un fichier « .jfif » —
    /// l'extension que Windows donne aux JPEG enregistrés depuis un navigateur —
    /// est accepté sans qu'on ait à le prévoir : c'est du JPEG, il commence par
    /// la signature du JPEG, il est rangé en « .jpg ». Et c'est aussi ce qui
    /// arrête une page HTML d'erreur qu'un serveur aurait annoncée comme une
    /// image.
    /// </summary>
    private async Task<string> RangerAsync(byte[] octets, CancellationToken token)
    {
        if (octets.Length < TailleMinimale) return string.Empty;

        var extension = FormatImage.Extension(octets);
        if (extension.Length == 0) return string.Empty;

        Directory.CreateDirectory(Dossier);

        // Un nom tiré au hasard, et non l'ISBN : 30 % des albums n'en ont pas,
        // et deux exemplaires du même livre sont deux lignes qui peuvent porter
        // deux images différentes (deux éditions).
        var nom = $"{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(Dossier, nom), octets, token);

        return nom;
    }
}
