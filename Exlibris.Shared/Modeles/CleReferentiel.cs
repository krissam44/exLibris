using System.Globalization;
using System.Text;

namespace Exlibris.Shared.Modeles;

/// <summary>
/// La clé de rapprochement d'un éditeur.
///
/// Le catalogue est PARTAGÉ : quand une association catalogue un ouvrage à la
/// main, faute de retour des API, elle saisit le nom de l'éditeur librement. La
/// contrainte d'unicité ne portait que sur le libellé exact, si bien que
/// « Gallimard », « gallimard » et « Éditions Gallimard » faisaient trois
/// maisons distinctes dans un référentiel censé être commun.
///
/// Cette clé les ramène à la même valeur. Elle sert de seconde contrainte
/// d'unicité : la création d'un éditeur converge alors d'elle-même vers la
/// fiche existante, sans rien demander au bénévole.
///
/// <b>Elle est délibérément timide.</b> Elle ne retire que ce qui ne distingue
/// jamais deux maisons — la casse, les accents, la ponctuation, et le mot
/// « Éditions » lui-même. Elle ne touche PAS aux qualificatifs : « Gallimard »
/// et « Gallimard Jeunesse » sont deux éditeurs, « Actes Sud » et « Actes Sud
/// Junior » aussi. Un rapprochement trop zélé fusionnerait des catalogues
/// entiers, et rien ne le rattraperait.
///
/// Elle ne corrige pas non plus les fautes de frappe : « Galimard » avec un seul
/// L reste une maison à part. Une comparaison approchée (distance d'édition)
/// rapprocherait aussi « Delcourt » et « Delacour », qui n'ont rien à voir. Le
/// risque de fusion abusive y dépasse le service rendu.
/// </summary>
public static class CleReferentiel
{
    /// <summary>
    /// Mots retirés en TÊTE du libellé uniquement.
    ///
    /// En tête, « Éditions » est un simple préfixe d'usage ; ailleurs, il fait
    /// partie du nom (« Nouvelles Éditions Latines »). La position compte donc
    /// autant que le mot.
    /// </summary>
    private static readonly string[] PrefixesUsuels =
    [
        "les editions ", "les edition ", "editions ", "edition ", "les ed ", "ed ", "eds "
    ];

    /// <summary>
    /// Rend la forme comparable d'un nom d'éditeur. Chaîne vide pour une entrée
    /// vide — l'appelant décide alors qu'il n'y a pas d'éditeur connu.
    /// </summary>
    public static string Editeur(string? nom)
    {
        if (string.IsNullOrWhiteSpace(nom)) return string.Empty;

        // 1. Accents retirés par décomposition Unicode : « é » devient « e » +
        //    un diacritique combinant, qu'on écarte.
        var sansAccent = new string(nom.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        // 2. Tout ce qui n'est ni lettre ni chiffre devient une espace : cela
        //    couvre d'un coup les points, tirets, apostrophes et « & ».
        var alphanumerique = new string(sansAccent
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ')
            .ToArray());

        // 3. Espaces multiples réduits — indispensable avant de reconnaître un
        //    préfixe, « Éd.  Gallimard » ayant produit deux espaces à l'étape 2.
        var compacte = string.Join(' ', alphanumerique.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // 4. Préfixe d'usage. Une seule passe : « Éditions Éditions X » n'existe
        //    pas, et boucler ferait disparaître un nom légitimement composé.
        foreach (var prefixe in PrefixesUsuels)
        {
            if (!compacte.StartsWith(prefixe, StringComparison.Ordinal)) continue;

            var reste = compacte[prefixe.Length..];

            // Garde-fou : on ne retire jamais le préfixe s'il ne reste rien.
            // Une maison qui s'appelle littéralement « Éditions » garde son nom
            // plutôt que de se voir attribuer une clé vide, qui la confondrait
            // avec toutes les autres entrées vides.
            if (reste.Length > 0) return reste;
            break;
        }

        return compacte;
    }

    /// <summary>
    /// Reproduit, côté C#, la comparaison que MariaDB fait sur un référentiel :
    /// `utf8mb4_general_ci` ignore la CASSE <b>et les ACCENTS</b>.
    ///
    /// <b>Le défaut que ceci corrige.</b> <c>CategorieRepository</c> de Lirevia
    /// tenait son index en mémoire avec <c>StringComparer.OrdinalIgnoreCase</c>,
    /// sous un commentaire affirmant que c'était « comme la clé unique de la
    /// table ». Ça ne l'est pas : `OrdinalIgnoreCase` ignore la casse
    /// SEULEMENT. Un libellé ne différant d'un existant que par ses accents —
    /// « Litterature » face à « Littérature » — manquait donc l'index, partait en
    /// base, y heurtait la clé unique… et InnoDB <b>consomme une valeur
    /// d'auto-incrément même quand la ligne existe déjà</b> : il réserve le numéro
    /// avant de découvrir le doublon et ne le rend jamais.
    ///
    /// Aucune ligne créée, un numéro brûlé, à chaque appel. Constaté le
    /// 22/08/2026 : après une renumérotation propre s'arrêtant à 34, la catégorie
    /// suivante a pris l'identifiant <b>203</b>.
    ///
    /// <b>Ce repli est volontairement TIMIDE.</b> Se tromper dans un sens coûte un
    /// numéro ; se tromper dans l'autre rend un id ERRONÉ — deux libellés que la
    /// base distingue seraient confondus, et des ouvrages atterriraient dans la
    /// mauvaise rubrique. On ne replie donc que ce dont on est sûr : la casse et
    /// les accents. `utf8mb4_general_ci` replie un peu plus (le ß allemand vaut
    /// « s »), et c'est très bien : l'index manquera ces cas-là et repassera par
    /// la base, qui tranchera juste.
    ///
    /// La normalisation NFC précède le retrait des diacritiques, sans quoi un « e »
    /// suivi d'un accent combinant serait replié sur « e » alors que la base le
    /// distingue — précisément l'erreur qu'on refuse de commettre.
    /// </summary>
    public static string CleCollation(string? valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur)) return string.Empty;

        var compose = valeur.Trim().Normalize(NormalizationForm.FormC);

        var sansAccent = new string(compose.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        return sansAccent.ToLowerInvariant();
    }

    /// <summary>
    /// Ramène un libellé de référentiel à la longueur de sa colonne.
    ///
    /// <b>Pourquoi ce n'est pas facultatif.</b> `editeurs.nom` tient 150
    /// caractères, `categories.nom` 80 ; au-delà, MariaDB ne tronque pas, il
    /// REFUSE (« Data too long for column »). L'insertion lève, et comme elle est
    /// au milieu d'une reprise de fonds, c'est toute la migration d'une
    /// bibliothèque qui s'arrête sur une notice. Constaté le 21/08/2026 sur le
    /// réservoir BnF, à la 49 500ᵉ ligne.
    ///
    /// Les coupables sont rares mais parfaitement légitimes : des éditeurs
    /// institutionnels (« Direction générale de l'éducation et des
    /// enseignements, Ministère… », 167 caractères) et des vedettes RAMEAU en
    /// zone 606, que l'import UNIMARC verse dans la catégorie (« Certificat
    /// d'aptitude aux fonctions d'encadrement… », 98). Six éditeurs et 44
    /// catégories sur 539 410 notices — assez pour arrêter une migration sur
    /// trois, pas assez pour qu'on les voie venir.
    ///
    /// La coupe se fait au dernier séparateur de mots, pas au caractère près :
    /// un libellé de référentiel s'affiche tel quel dans les listes, et « …
    /// Ministère de l'action et des comptes pu » y ferait plus désordre que la
    /// phrase amputée d'un mot de plus.
    /// </summary>
    public static string Tronquer(string? nom, int longueurMax)
    {
        var propre = (nom ?? "").Trim();
        if (propre.Length <= longueurMax) return propre;

        var coupe = propre[..longueurMax];

        // On ne recule qu'à portée raisonnable : si le dernier séparateur est
        // loin derrière, mieux vaut une coupe nette que perdre la moitié du
        // libellé — un nom sans espace sur 150 caractères existe (une URL
        // recopiée dans le champ, par exemple).
        var separateur = coupe.LastIndexOfAny([' ', ',', ';']);
        if (separateur >= longueurMax - 30) coupe = coupe[..separateur];

        return coupe.TrimEnd(' ', ',', ';', '.', '-');
    }
}
