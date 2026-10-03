using System.Text.Json;
using System.Xml.Linq;
using Exlibris.Shared.Modeles;

namespace Exlibris.Data;

/// <summary>
/// Cherche une notice à partir d'un ISBN, sur les sources publiques gratuites.
///
/// <b>La lecture du réseau et l'ANALYSE du contenu sont séparées</b>, et c'est
/// la seule décision de structure qui compte ici. Les méthodes d'analyse sont
/// statiques et prennent une chaîne : elles s'éprouvent sur des réponses
/// enregistrées, sans réseau, donc de façon reproductible. Une suite qui
/// appellerait vraiment Google échouerait un jour sur un quota, une panne ou un
/// train sans connexion — et on finirait par ne plus la croire.
///
/// <b>Quatre sources, dans cet ordre : Lirevia, BnF, Google, Open Library.</b>
/// La première qui répond avec un titre gagne — sur une saisie interactive, on
/// veut une réponse en une seconde, pas la meilleure des quatre.
///
/// <b>Lirevia n'est interrogée que si son adresse est configurée</b>, ce qui
/// n'est le cas que chez son auteur : son catalogue porte le réservoir BnF
/// déjà versé, les couvertures déjà rapportées et les corrections faites à la
/// main. Ailleurs, la chaîne commence à la BnF sans que rien ne le signale.
///
/// <b>La BnF vient ensuite, et le classement n'est pas arbitraire.</b> Le dépôt
/// légal lui donne tout ce qui paraît en France : elle connaît les romans de
/// poche et les albums que les bases anglophones ignorent. Mesuré sur
/// 9782253106654 (Musso, Livre de poche) — BnF le trouve, Open Library répond
/// « inconnu », et Google refuse l'appel.
///
/// ⚠️ <b>Google est peu fiable sans clé d'API.</b> Les appels anonymes partagent
/// un quota global, régulièrement épuisé : la réponse est alors un 429 que ce
/// service traite comme un silence. Il reste utile en complément, jamais comme
/// source principale.
///
/// L'interrogation de la BnF se fait en <c>recordSchema=dublincore</c>, qui rend
/// des champs plats (titre, auteur, éditeur, date). L'UNIMARC que sert son
/// schéma par défaut demanderait dix fois ce code pour le même résultat.
/// </summary>
public class NoticeEnLigneService(HttpClient client, string adresseLirevia = "")
{
    /// <summary>
    /// Adresse de l'API Lirevia, ou vide.
    ///
    /// <b>Vide par défaut, et c'est essentiel</b> : Ex-libris est destiné à
    /// être distribué, et la plupart de ceux qui l'installeront n'auront jamais
    /// entendu parler de Lirevia. La source est donc simplement SAUTÉE quand
    /// l'adresse manque — pas d'appel, pas d'attente, pas de message d'erreur
    /// à propos d'un service qui ne les concerne pas.
    /// </summary>
    private readonly string _lirevia = (adresseLirevia ?? "").TrimEnd('/');

    /// <summary>
    /// La notice trouvée, ou <c>null</c>. Ne lève JAMAIS pour une raison de
    /// réseau : sur un formulaire de saisie, une source injoignable doit se
    /// solder par « rien trouvé » et laisser continuer à la main, pas par une
    /// fenêtre d'erreur au milieu du catalogage.
    /// </summary>
    public Task<NoticeEnLigne?> ChercherAsync(string? isbnSaisi, CancellationToken token = default) =>
        ChercherAsync(isbnSaisi, url => LireAsync(url, token), _lirevia);

    /// <summary>
    /// L'ENCHAÎNEMENT des sources, séparé de leur lecture.
    ///
    /// La méthode prend de quoi lire une URL au lieu d'aller la chercher
    /// elle-même, et c'est ce qui rend l'ordre vérifiable : un test lui fournit
    /// des réponses toutes prêtes et note quelles adresses ont été demandées.
    ///
    /// Le découpage vient d'un défaut réel. L'ISBN 9782253106654 restait
    /// introuvable parce que la BnF n'était pas interrogée ; quand la correction
    /// a été éprouvée, retirer de nouveau la BnF de la chaîne n'a fait échouer
    /// AUCUN test — l'ordre des sources vivait dans une méthode que rien ne
    /// pouvait atteindre. Un correctif dont on ne peut pas montrer qu'il tient
    /// n'est qu'une intention.
    /// </summary>
    internal static async Task<NoticeEnLigne?> ChercherAsync(
        string? isbnSaisi, Func<string, Task<string>> lire, string lirevia = "")
    {
        var isbn = Isbn.Normaliser(isbnSaisi);
        if (isbn.Length == 0) return null;

        // Lirevia D'ABORD, quand elle est configurée. Son catalogue porte le
        // réservoir BnF déjà versé, ses couvertures déjà rapportées et les
        // corrections faites à la main : elle rend en une requête ce que les
        // sources publiques donnent en trois, quand elles le donnent.
        if (lirevia.Length > 0)
        {
            var chezNous = AnalyserLirevia(await lire($"{lirevia}/api/public/notices/{isbn}"));
            if (chezNous is not null) return chezNous;
        }

        // Puis la BnF : le dépôt légal lui donne tout ce qui paraît en France,
        // là où les bases anglophones ignorent le roman de poche et l'album
        // francophone.
        var bnf = AnalyserBnf(await lire(
            "https://catalogue.bnf.fr/api/SRU?version=1.2&operation=searchRetrieve"
            + $"&query=bib.isbn%20all%20%22{isbn}%22&recordSchema=dublincore&maximumRecords=1"));
        if (bnf is not null) return bnf;

        var google = AnalyserGoogle(await lire(
            $"https://www.googleapis.com/books/v1/volumes?q=isbn:{isbn}&country=FR"));
        if (google is not null) return google;

        return AnalyserOpenLibrary(await lire(
            $"https://openlibrary.org/api/books?bibkeys=ISBN:{isbn}&format=json&jscmd=data"), isbn);
    }

    private async Task<string> LireAsync(string url, CancellationToken token)
    {
        try
        {
            using var reponse = await client.GetAsync(url, token);
            return reponse.IsSuccessStatusCode
                ? await reponse.Content.ReadAsStringAsync(token)
                : string.Empty;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Hors ligne, DNS muet, délai dépassé : autant de « rien trouvé ».
            return string.Empty;
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  Analyse — statique et sans réseau, donc éprouvable
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// La notice rendue par l'API publique de Lirevia.
    ///
    /// <b>Le format est le nôtre</b>, contrairement aux trois autres sources :
    /// c'est le seul cas où les deux côtés du fil sont à nous. La tentation
    /// serait de partager la classe entre les deux projets ; on ne le fait pas,
    /// et pour une raison précise — Ex-libris doit continuer de fonctionner
    /// contre une version de Lirevia plus ancienne ou plus récente que lui. Une
    /// classe partagée transformerait toute évolution du serveur en obligation
    /// de mise à jour simultanée des trois postes.
    ///
    /// La SOURCE d'origine est reportée telle quelle (« BnF », « Google Books »),
    /// et non remplacée par « Lirevia » : Lirevia est le chemin par lequel la
    /// notice nous arrive, pas son auteur. C'est aussi ce que demande la Licence
    /// Ouverte — l'attribution voyage avec la donnée, elle ne s'arrête pas au
    /// premier intermédiaire.
    /// </summary>
    internal static NoticeEnLigne? AnalyserLirevia(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var f = doc.RootElement;

            if (f.ValueKind != JsonValueKind.Object) return null;

            var notice = new NoticeEnLigne
            {
                Titre         = Texte(f, "titre"),
                Auteurs       = Texte(f, "auteur"),
                Editeur       = Texte(f, "editeur"),
                // ⚠️ « categorie » et non « genre » : c'est la clé que rend
                // l'API de Lirevia, et ce format lui appartient. Ex-libris a
                // renommé sa propre notion ; le contrat entre les deux
                // applications, lui, ne se renomme pas sans coordonner un
                // déploiement des deux côtés.
                Genre         = Texte(f, "categorie"),
                CouvertureUrl = Texte(f, "couvertureUrl"),
                Source        = Texte(f, "source") is { Length: > 0 } s ? s : "Lirevia",
            };

            if (f.TryGetProperty("anneeEdition", out var annee) && annee.TryGetInt32(out var a))
                notice.Annee = a;

            return notice.EstUtilisable ? notice : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static NoticeEnLigne? AnalyserBnf(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        try
        {
            var document = XDocument.Parse(xml);
            XNamespace dc = "http://purl.org/dc/elements/1.1/";

            var fiche = document.Descendants(dc + "title").FirstOrDefault()?.Parent;
            if (fiche is null) return null;

            var notice = new NoticeEnLigne
            {
                Titre   = Valeur(fiche, dc + "title"),
                Editeur = SansLieu(Valeur(fiche, dc + "publisher")),
                Annee   = ExtraireAnnee(Valeur(fiche, dc + "date")),
                Source  = "BnF",
            };

            notice.CouvertureUrl = CouvertureBnf(fiche.Elements(dc + "identifier").Select(e => e.Value));

            // La BnF ne donne pas de genre exploitable : `dc:description` y porte
            // la collection éditoriale (« Collection : Littérature »), qui n'est
            // pas la même chose. Laissé vide plutôt que de verser une valeur
            // approximative dans le référentiel des genres.
            notice.Auteurs = string.Join(", ", fiche.Elements(dc + "creator")
                .Select(e => NomLisible(e.Value))
                .Where(n => n.Length > 0));

            return notice.EstUtilisable ? notice : null;
        }
        catch (System.Xml.XmlException)
        {
            // Réponse tronquée ou page d'erreur : traitée comme un silence.
            return null;
        }
    }

    /// <summary>
    /// « Musso, Guillaume (1974-....) » → « Guillaume Musso ».
    ///
    /// <b>Les deux transformations sont nécessaires, pour deux raisons
    /// différentes.</b> Les dates de vie entre parenthèses n'ont pas leur place
    /// dans un champ de saisie. Et surtout, l'inversion : le formulaire découpe
    /// ses auteurs SUR LA VIRGULE, si bien que « Musso, Guillaume » y créerait
    /// deux auteurs — « Musso » et « Guillaume » — sans que rien ne le signale.
    /// La forme « Prénom Nom » est aussi celle que rendent Google et Open
    /// Library, donc les trois sources alimentent le même référentiel.
    /// </summary>
    internal static string NomLisible(string brut)
    {
        var sansDates = brut.Split('(')[0].Trim().TrimEnd(',').Trim();

        var membres = sansDates.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Deux membres exactement : le cas « Nom, Prénom » du catalogage. Plus
        // que deux, on ne sait pas ce qu'on lit — mieux vaut ne rien inventer.
        return membres.Length == 2 ? $"{membres[1]} {membres[0]}" : sansDates;
    }

    /// <summary>
    /// « L.G.F - Le livre de poche  (Paris) » → « L.G.F - Le livre de poche ».
    /// Le lieu d'édition est une notion de catalogage, pas un nom de maison, et
    /// il ferait deux éditeurs distincts du même selon qu'il est présent ou non.
    /// </summary>
    internal static string SansLieu(string brut)
    {
        var sansParenthese = brut.Split('(')[0];
        return string.Join(' ', sansParenthese.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// L'adresse de la couverture chez la BnF, déduite de l'identifiant ark.
    ///
    /// C'est la seule adresse FABRIQUÉE du service, et elle mérite sa
    /// justification : la réponse SRU ne porte aucun lien d'image, mais le
    /// service de vignettes de la BnF accepte l'ark de la notice. Vérifié le
    /// 29/08/2026 sur ark:/12148/cb48736742q — un JPEG de 600 × 966.
    ///
    /// L'ark se trouve parmi PLUSIEURS « dc:identifier » : la même balise porte
    /// aussi l'ISBN et le code-barres commercial. D'où le filtre sur le préfixe,
    /// plutôt qu'une lecture du premier venu.
    /// </summary>
    internal static string CouvertureBnf(IEnumerable<string> identifiants)
    {
        var ark = identifiants
            .Select(i => i.Trim())
            .FirstOrDefault(i => i.Contains("ark:/", StringComparison.Ordinal));

        if (ark is null) return string.Empty;

        var debut = ark.IndexOf("ark:/", StringComparison.Ordinal);
        return $"https://catalogue.bnf.fr/couverture?&appName=NE&idArk={ark[debut..]}&couverture=1";
    }

    /// <summary>
    /// La vignette de Google, en plus grand. Reprise de Lirevia, où la mesure a
    /// été faite : « zoom=1 » rend 128 px de large, « zoom=2 » environ 300.
    ///
    /// <b>Rien n'est fabriqué</b> : on ajuste un paramètre de l'adresse que
    /// Google a donnée, et une adresse qui ne porte pas « zoom=1 » ressort
    /// inchangée — c'est ce qui rend la substitution sûre. « edge=curl » dessine
    /// une page cornée sur l'image : une décoration de vignette de recherche,
    /// qui mange un coin du visuel.
    ///
    /// Le passage en https n'est pas cosmétique : Google sert encore des
    /// adresses en clair, que WPF refuse de charger sur certaines
    /// configurations.
    /// </summary>
    internal static string HauteResolution(string url)
    {
        if (!url.Contains("books.google", StringComparison.OrdinalIgnoreCase)) return url;

        return url.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase)
                  .Replace("zoom=1", "zoom=2", StringComparison.Ordinal)
                  .Replace("&edge=curl", "", StringComparison.OrdinalIgnoreCase);
    }

    private static string Valeur(XElement parent, XName nom) =>
        parent.Elements(nom).FirstOrDefault()?.Value.Trim() ?? string.Empty;

    internal static NoticeEnLigne? AnalyserGoogle(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
                return null;

            var info = items[0].GetProperty("volumeInfo");

            var notice = new NoticeEnLigne
            {
                Titre   = Texte(info, "title"),
                Editeur = Texte(info, "publisher"),
                Annee   = ExtraireAnnee(Texte(info, "publishedDate")),
                Source  = "Google Books",
            };

            // Le sous-titre fait partie du titre pour un lecteur, pas pour
            // Google : « Le Lotus bleu » et « Les Aventures de Tintin » y sont
            // deux champs. Recollés, sinon le titre relu paraît tronqué.
            var sousTitre = Texte(info, "subtitle");
            if (sousTitre.Length > 0 && notice.Titre.Length > 0)
                notice.Titre = $"{notice.Titre} — {sousTitre}";

            notice.Auteurs = Concatener(info, "authors");
            notice.Genre = PremiereGenre(Concatener(info, "genres"));

            if (info.TryGetProperty("imageLinks", out var images))
                notice.CouvertureUrl = HauteResolution(Texte(images, "thumbnail"));

            return notice.EstUtilisable ? notice : null;
        }
        catch (JsonException)
        {
            // Réponse tronquée ou page d'erreur HTML : traitée comme un silence.
            return null;
        }
    }

    internal static NoticeEnLigne? AnalyserOpenLibrary(string json, string isbn)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);

            // La réponse est indexée par la clé demandée, pas par un tableau :
            // un objet VIDE ({}) signifie « inconnu », et c'est le cas le plus
            // fréquent pour la bande dessinée française.
            if (!doc.RootElement.TryGetProperty($"ISBN:{isbn}", out var fiche))
                return null;

            var notice = new NoticeEnLigne
            {
                Titre  = Texte(fiche, "title"),
                Annee  = ExtraireAnnee(Texte(fiche, "publish_date")),
                Source = "Open Library",
            };

            notice.Auteurs = NomsDeObjets(fiche, "authors");
            notice.Editeur = NomsDeObjets(fiche, "publishers");
            notice.Genre = PremiereGenre(NomsDeObjets(fiche, "subjects"));

            // L'objet « cover » de la réponse, et NON une adresse fabriquée à
            // partir de l'ISBN. Mesuré le 29/08/2026 : covers.openlibrary.org
            // répond 200 avec 43 octets — une image blanche — pour un ouvrage
            // qu'il ne connaît pas. On stockerait une couverture vide en croyant
            // en avoir une. Le champ « cover » n'apparaît que s'il y a vraiment
            // quelque chose derrière, ce qui évite d'avoir à le vérifier.
            if (fiche.TryGetProperty("cover", out var couverture))
                notice.CouvertureUrl = Texte(couverture, "large") is { Length: > 0 } grande
                    ? grande
                    : Texte(couverture, "medium");

            return notice.EstUtilisable ? notice : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // -----------------------------------------------------------------------------------------------------------------

    private static string Texte(JsonElement parent, string nom) =>
        parent.TryGetProperty(nom, out var valeur) && valeur.ValueKind == JsonValueKind.String
            ? valeur.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>Un tableau de chaînes, recollé : « Van Hamme, Rosiński ».</summary>
    private static string Concatener(JsonElement parent, string nom) =>
        parent.TryGetProperty(nom, out var tableau) && tableau.ValueKind == JsonValueKind.Array
            ? string.Join(", ", tableau.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s)))
            : string.Empty;

    /// <summary>
    /// Open Library enveloppe ses listes dans des objets <c>{ "name": … }</c>,
    /// là où Google donne des chaînes nues. D'où deux extracteurs plutôt qu'un
    /// seul qui essaierait de deviner la forme.
    /// </summary>
    private static string NomsDeObjets(JsonElement parent, string nom) =>
        parent.TryGetProperty(nom, out var tableau) && tableau.ValueKind == JsonValueKind.Array
            ? string.Join(", ", tableau.EnumerateArray()
                .Select(e => Texte(e, "name"))
                .Where(s => s.Length > 0))
            : string.Empty;

    /// <summary>
    /// La première genre, débarrassée de sa hiérarchie BISAC : Google rend
    /// « Comics &amp; Graphic Novels / European », dont seul le premier membre
    /// éclaire quelque chose. La suite se lit mal dans une liste déroulante et
    /// se retrouverait telle quelle dans le référentiel des genres.
    /// </summary>
    private static string PremiereGenre(string brut)
    {
        var premiere = brut.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                           .FirstOrDefault() ?? string.Empty;

        return premiere.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// L'année d'une date de publication de forme libre : « 2019 »,
    /// « 2019-04-11 », « April 2019 », « c1985 ». On cherche quatre chiffres
    /// consécutifs plausibles plutôt que d'espérer un format.
    /// </summary>
    internal static int? ExtraireAnnee(string valeur)
    {
        for (var i = 0; i + 4 <= valeur.Length; i++)
        {
            var tranche = valeur.AsSpan(i, 4);
            if (!int.TryParse(tranche, out var annee)) continue;

            // Bornes larges : l'imprimerie pour plancher, et de quoi accepter
            // une réédition annoncée. Elles n'écartent que les nombres qui ne
            // sont visiblement pas des années — un numéro de collection, par
            // exemple, qu'on prendrait sinon pour une date.
            if (annee is >= 1450 and <= 2100) return annee;
        }

        return null;
    }
}
