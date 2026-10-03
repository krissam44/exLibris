using System.Text;

namespace Exlibris.Shared.Modeles;

/// <summary>
/// Normalisation et contrôle d'un ISBN, tel qu'il sort d'une douchette.
///
/// <b>Pourquoi ce n'est pas un simple <c>Trim()</c>.</b> Le code-barres d'un
/// livre est un EAN-13, et sur une bande dessinée il est souvent suivi, à sa
/// droite, d'un <b>code additionnel</b> de 2 ou 5 chiffres — le prix, ou un
/// numéro de série. Certaines douchettes le concatènent au principal. On reçoit
/// alors un « ISBN » de 15 ou 18 chiffres qui n'est pas invalide au sens du
/// scanner : il est simplement faux, et aucune base publique ne le connaîtra.
///
/// La normalisation vit ICI, dans le projet partagé, et non dans l'application
/// mobile : <b>le scanner doit rester bête</b>. Il enregistre tout, ne valide
/// rien, ne dédoublonne pas. Un scanner qui refuserait un code déjà vu ferait
/// manquer les doublons réels — ces deux exemplaires qu'on possède vraiment.
/// </summary>
public static class Isbn
{
    /// <summary>
    /// Ramène une saisie à sa forme canonique : chiffres et « X » final
    /// seulement, code additionnel retiré. Chaîne vide si rien d'exploitable —
    /// l'appelant écrit alors <c>NULL</c> en base, jamais une chaîne vide.
    /// </summary>
    public static string Normaliser(string? saisie)
    {
        if (string.IsNullOrWhiteSpace(saisie)) return string.Empty;

        // Tirets, espaces et points sautent : « 978-2-8036-8003-0 » et
        // « 9782803680030 » sont le même livre.
        var propre = new StringBuilder(saisie.Length);
        foreach (var c in saisie.Trim())
        {
            if (char.IsAsciiDigit(c)) propre.Append(c);
            // Le X de contrôle d'un ISBN-10 vaut 10. Il n'apparaît qu'en
            // DERNIÈRE position ; ailleurs, c'est une saisie fautive.
            else if (c is 'X' or 'x') propre.Append('X');
        }

        var chiffres = propre.ToString();

        // Le code additionnel : un EAN-13 suivi de 2 ou 5 chiffres. On ne coupe
        // QUE dans ces deux cas précis, et seulement si les 13 premiers forment
        // un EAN valide — sans quoi on tronquerait une saisie simplement fausse
        // en la rendant plausible, ce qui est pire.
        if (chiffres.Length is 15 or 18 && CleValide(chiffres[..13]))
            chiffres = chiffres[..13];

        return chiffres;
    }

    /// <summary>
    /// L'ISBN est-il complet et sa clé de contrôle juste ?
    ///
    /// Sert à SIGNALER une saisie douteuse, jamais à la refuser : un album
    /// réellement présent sur l'étagère doit pouvoir entrer au catalogue même
    /// si son code-barres est illisible ou fantaisiste. Le contrôle dit
    /// « vérifiez », pas « non ».
    /// </summary>
    public static bool EstValide(string? saisie)
    {
        var propre = Normaliser(saisie);
        return propre.Length switch
        {
            13 => CleValide(propre),
            10 => CleValide10(propre),
            _  => false
        };
    }

    /// <summary>
    /// Ce code-barres est-il celui d'un LIVRE, et non d'une boîte de conserve ?
    ///
    /// <b>Plus strict que <see cref="EstValide"/>, délibérément.</b> La clé de
    /// contrôle d'un EAN-13 ne dit rien du contenu : un paquet de café la
    /// franchit aussi bien qu'un roman. Or les livres occupent une plage
    /// réservée — le « Bookland », préfixes 978 et 979 —, seul indice qui
    /// distingue un ouvrage d'une étiquette de prix ou du code de catégorie
    /// imprimé juste au-dessus du code-barres.
    ///
    /// Sert au SCANNER, qui doit trier ce que la caméra attrape au vol.
    /// <see cref="EstValide"/> reste la règle de la saisie au clavier, où l'on
    /// sait ce que l'on tape : elle avertit, elle ne refuse pas.
    ///
    /// L'ISBN-10 passe sans préfixe : il n'en porte pas, et se saisit encore à
    /// la main depuis la page de garde d'un livre ancien.
    /// </summary>
    public static bool EstLivre(string? saisie)
    {
        var propre = Normaliser(saisie);

        return propre.Length switch
        {
            13 => CleValide(propre)
                  && (propre.StartsWith("978", StringComparison.Ordinal)
                      || propre.StartsWith("979", StringComparison.Ordinal)),
            10 => CleValide10(propre),
            _  => false
        };
    }

    /// <summary>Clé de contrôle EAN-13 : somme pondérée 1-3, total multiple de 10.</summary>
    private static bool CleValide(string isbn13)
    {
        if (isbn13.Length != 13 || !isbn13.All(char.IsAsciiDigit)) return false;

        var somme = 0;
        for (var i = 0; i < 12; i++)
            somme += (isbn13[i] - '0') * (i % 2 == 0 ? 1 : 3);

        return (10 - somme % 10) % 10 == isbn13[12] - '0';
    }

    /// <summary>
    /// Clé de contrôle ISBN-10 : somme pondérée 10..1, total multiple de 11.
    /// Le dernier caractère peut valoir « X » pour 10 — d'où sa conservation
    /// par <see cref="Normaliser"/>.
    /// </summary>
    private static bool CleValide10(string isbn10)
    {
        if (isbn10.Length != 10) return false;

        var somme = 0;
        for (var i = 0; i < 9; i++)
        {
            if (!char.IsAsciiDigit(isbn10[i])) return false;
            somme += (isbn10[i] - '0') * (10 - i);
        }

        var dernier = isbn10[9];
        var controle = dernier == 'X' ? 10 : char.IsAsciiDigit(dernier) ? dernier - '0' : -1;
        if (controle < 0) return false;

        return (somme + controle) % 11 == 0;
    }
}
