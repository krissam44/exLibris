using System.Text.Json;

namespace Exlibris.Scanner.Services;

/// <summary>
/// Demande à Lirevia le titre d'un ISBN, pour l'afficher pendant qu'on scanne.
///
/// <b>Purement CONFORT, et jamais bloquant.</b> Le relevé n'a besoin que de
/// l'ISBN et du rangement ; le titre ne sert qu'à voir ce qu'on fait. Sur trois
/// cents ouvrages, c'est pourtant la différence entre une liste de chiffres et
/// une liste de livres — et c'est ce qui permet de repérer sur-le-champ qu'on
/// a scanné deux fois le code-barres du même volume, ou celui de la jaquette
/// d'à côté.
///
/// <b>Aucune conséquence si elle échoue</b> : hors réseau, chez quelqu'un
/// d'autre, ou simplement sans adresse configurée, la ligne porte l'ISBN nu et
/// le scan continue. C'est pourquoi le délai est court — deux secondes, pas
/// huit : mieux vaut une ligne sans titre qu'un scanner qui marque un temps
/// d'arrêt à chaque livre.
/// </summary>
public class RechercheTitre(InventaireEnCours inventaire)
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>
    /// Le titre, ou chaîne vide. Ne lève jamais.
    /// </summary>
    public async Task<string> ChercherAsync(string isbn, CancellationToken token = default)
    {
        var adresse = inventaire.AdresseLirevia;
        if (adresse.Length == 0) return string.Empty;

        try
        {
            using var reponse = await _client.GetAsync(
                $"{adresse}/api/public/notices/{isbn}", token);

            if (!reponse.IsSuccessStatusCode) return string.Empty;

            using var doc = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync(token));

            var titre = Lire(doc.RootElement, "titre");
            if (titre.Length == 0) return string.Empty;

            var auteur = Lire(doc.RootElement, "auteur");
            return auteur.Length > 0 ? $"{titre} — {auteur}" : titre;
        }
        catch (Exception)
        {
            // Réseau absent, serveur éteint, réponse illisible : autant de
            // « pas de titre ». Le catch est volontairement large — cette
            // fonction ne doit JAMAIS interrompre un scan.
            return string.Empty;
        }
    }

    /// <summary>
    /// Le même appel, mais qui DIT ce qui s'est passé.
    ///
    /// <see cref="ChercherAsync"/> avale toutes ses erreurs — c'est voulu, elle
    /// ne doit jamais interrompre un scan. Mais ce silence rend indiscernables
    /// une adresse fautive, un serveur éteint et un blocage d'Android : sans ce
    /// diagnostic, « cela ne semble rien faire » est tout ce qu'on peut
    /// constater.
    /// </summary>
    public async Task<(bool Ok, string Detail)> DiagnostiquerAsync(string isbn)
    {
        var adresse = inventaire.AdresseLirevia;
        var url = $"{adresse}/api/public/notices/{isbn}";

        try
        {
            using var reponse = await _client.GetAsync(url);

            if (!reponse.IsSuccessStatusCode)
                return (false, $"Le serveur répond {(int)reponse.StatusCode} sur\n{url}\n\n"
                             + "Vérifiez le port : l'API écoute sur 5002, le site sur 5590.");

            using var doc = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync());
            var titre = Lire(doc.RootElement, "titre");

            return titre.Length > 0
                ? (true, $"Lirevia répond : « {titre} ».\n\nLes titres s'afficheront pendant le scan.")
                : (false, "Le serveur répond, mais sans titre exploitable.");
        }
        catch (TaskCanceledException)
        {
            return (false, $"Aucune réponse en deux secondes depuis\n{url}\n\n"
                         + "Le serveur est-il allumé, et le téléphone sur le même réseau "
                         + "que lui ? En 4G, une adresse en 192.168 est injoignable.");
        }
        catch (HttpRequestException e)
        {
            // Le cas le plus fréquent, et le moins évident : Android REFUSE le
            // HTTP en clair depuis la version 9. L'appel n'atteint jamais le
            // réseau, et le message parle de « cleartext ».
            var enClair = e.Message.Contains("Cleartext", StringComparison.OrdinalIgnoreCase)
                       || e.Message.Contains("CLEARTEXT", StringComparison.Ordinal);

            return (false, enClair
                ? "Android refuse les connexions en clair (http). L'application doit "
                + "être recompilée avec l'autorisation correspondante."
                : $"Connexion impossible :\n{e.Message}");
        }
        catch (Exception e)
        {
            return (false, $"Réponse inattendue :\n{e.Message}");
        }
    }

    private static string Lire(JsonElement parent, string nom) =>
        parent.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
}
