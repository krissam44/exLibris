using System.Text;

// La classe porte une PROPRIÉTÉ Isbn, qui masque la classe du même nom. L'alias
// lève l'ambiguïté sans allonger l'appel ni renommer l'une des deux : le champ
// s'appelle bien Isbn, et le normalisateur est bien celui du projet.
using CodeIsbn = Exlibris.Shared.Modeles.Isbn;

namespace Exlibris.Shared.Modeles;

/// <summary>
/// Une ligne du fichier d'inventaire : ce que le téléphone a scanné devant une
/// étagère.
///
/// <b>Ce type vit dans Shared parce que DEUX applications doivent en avoir la
/// même lecture</b> : le scanner qui écrit le fichier, et Ex-libris qui
/// l'importe. Deux implémentations séparées se seraient contentées de marcher
/// jusqu'au premier rangement contenant un point-virgule — et le désaccord se
/// serait vu non pas à l'import, qui n'aurait rien signalé, mais des semaines
/// plus tard devant une étagère mal renseignée.
/// </summary>
public class LigneInventaire
{
    public string Isbn { get; set; } = string.Empty;

    /// <summary>
    /// Où l'ouvrage se trouve : « Salon, étagère 2 ».
    ///
    /// C'est LA raison d'être du scan sur place. Le titre, l'auteur et la
    /// couverture se retrouvent plus tard depuis n'importe quelle source ; le
    /// rangement, lui, n'est connu que de celui qui tient le livre. Le saisir
    /// après coup, sur trois cents fiches et sans les étagères sous les yeux,
    /// est un travail qu'on ne fait jamais.
    /// </summary>
    public string Rangement { get; set; } = string.Empty;

    /// <summary>Le séparateur : le point-virgule, qu'attend un Excel français.</summary>
    public const char Separateur = ';';

    /// <summary>
    /// La première ligne du fichier. Elle sert deux fois : elle rend le fichier
    /// lisible par un humain qui l'ouvre par curiosité, et elle permet à
    /// l'import de reconnaître un fichier qui n'est pas le sien plutôt que d'en
    /// avaler les colonnes à l'envers.
    /// </summary>
    public const string Entete = "isbn;rangement";

    // -----------------------------------------------------------------------------------------------------------------
    //  Écriture
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Le contenu complet du fichier, en-tête compris.
    ///
    /// <b>À écrire en UTF-8 AVEC signature</b> (BOM). Sans elle, Excel lit un
    /// fichier français en ANSI et affiche « étagère » : le fichier est
    /// pourtant correct, mais celui qui l'ouvre conclut que le scanner est
    /// cassé.
    /// </summary>
    public static string Composer(IEnumerable<LigneInventaire> lignes)
    {
        var texte = new StringBuilder();
        texte.AppendLine(Entete);

        foreach (var ligne in lignes)
            texte.AppendLine($"{Echapper(ligne.Isbn)}{Separateur}{Echapper(ligne.Rangement)}");

        return texte.ToString();
    }

    /// <summary>
    /// Protège une valeur qui contiendrait le séparateur ou un guillemet.
    ///
    /// « Salon; étagère 2 » est un rangement parfaitement légitime, et sans
    /// cette protection il produirait trois colonnes au lieu de deux — donc un
    /// rangement tronqué à l'import, silencieusement. Convention RFC 4180 :
    /// on entoure de guillemets et on double ceux qui se trouvent dedans.
    /// </summary>
    private static string Echapper(string valeur)
    {
        var propre = (valeur ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

        return propre.Contains(Separateur) || propre.Contains('"')
            ? $"\"{propre.Replace("\"", "\"\"")}\""
            : propre;
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  Lecture
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Les lignes exploitables d'un fichier. En-tête, lignes vides et lignes
    /// sans ISBN sont écartées <b>en silence</b>.
    ///
    /// Le silence est un choix : un fichier d'inventaire se termine souvent par
    /// une ligne vide, et refuser d'importer trois cents livres pour cela serait
    /// absurde. En revanche, un fichier dont AUCUNE ligne n'est exploitable doit
    /// se voir — c'est à l'appelant de comparer le compte obtenu à zéro.
    /// </summary>
    public static List<LigneInventaire> Analyser(string? contenu)
    {
        var lignes = new List<LigneInventaire>();
        if (string.IsNullOrWhiteSpace(contenu)) return lignes;

        foreach (var brute in contenu.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var ligne = brute.Trim();
            if (ligne.Length == 0) continue;

            // L'en-tête, quelle que soit sa casse. Comparé sur la première
            // colonne seule : un fichier relu et réenregistré par Excel peut
            // avoir gagné des colonnes vides en fin de ligne.
            if (ligne.StartsWith("isbn", StringComparison.OrdinalIgnoreCase) &&
                !char.IsDigit(ligne[^1]))
                continue;

            var champs = Decouper(ligne);

            var isbn = CodeIsbn.Normaliser(champs.ElementAtOrDefault(0));
            if (isbn.Length == 0) continue;

            lignes.Add(new LigneInventaire
            {
                Isbn = isbn,
                Rangement = champs.ElementAtOrDefault(1)?.Trim() ?? string.Empty,
            });
        }

        return lignes;
    }

    /// <summary>
    /// Découpe une ligne en respectant les guillemets — le pendant exact
    /// d'<see cref="Echapper"/>. Un simple <c>Split(';')</c> aurait suffi tant
    /// qu'aucun rangement ne porte de point-virgule, ce qui est le genre de
    /// supposition qui tient jusqu'au jour où elle ne tient plus.
    /// </summary>
    private static List<string> Decouper(string ligne)
    {
        var champs = new List<string>();
        var courant = new StringBuilder();
        var dansGuillemets = false;

        for (var i = 0; i < ligne.Length; i++)
        {
            var c = ligne[i];

            if (dansGuillemets)
            {
                if (c != '"') { courant.Append(c); continue; }

                // Un guillemet doublé à l'intérieur représente un guillemet.
                if (i + 1 < ligne.Length && ligne[i + 1] == '"') { courant.Append('"'); i++; }
                else dansGuillemets = false;
            }
            else if (c == '"' && courant.Length == 0) dansGuillemets = true;
            else if (c == Separateur) { champs.Add(courant.ToString()); courant.Clear(); }
            else courant.Append(c);
        }

        champs.Add(courant.ToString());
        return champs;
    }
}
