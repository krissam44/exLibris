using Dapper;
using Exlibris.Shared.Modeles;

namespace Exlibris.Data;

/// <summary>
/// Verse dans la base un inventaire relevé au téléphone.
///
/// <b>Une fiche importée n'a QUE son ISBN et son rangement.</b> Ni titre, ni
/// auteur, ni éditeur — et surtout pas un libellé provisoire du genre
/// « ISBN 978… » en guise de titre. La raison est mécanique :
/// <see cref="NoticeEnLigne.Completer"/> ne remplit que les champs VIDES, pour
/// ne jamais écraser ce que vous avez corrigé à la main. Un titre provisoire
/// serait donc un titre définitif, et trois cents fiches resteraient à jamais
/// nommées par leur code-barres.
///
/// Le titre vide est la vérité : on ne le connaît pas encore. L'affichage s'en
/// arrange (voir <see cref="Livre.TitreComplet"/>), et l'enrichissement le
/// remplira.
/// </summary>
public class InventaireDepot(FabriqueConnexion fabrique)
{
    /// <summary>Ce qu'un import a produit, pour le dire à l'écran.</summary>
    public record Bilan(int Crees, int DejaPresents)
    {
        public int Total => Crees + DejaPresents;
    }

    /// <summary>
    /// Crée une fiche par ISBN encore inconnu de la base.
    ///
    /// <b>Un ISBN déjà présent est IGNORÉ</b>, et c'est le point délicat. Le
    /// schéma autorise délibérément deux fiches de même ISBN — on peut posséder
    /// deux fois le même album. Mais réimporter le même fichier est un accident
    /// bien plus fréquent que le double exemplaire : par prudence on ne
    /// duplique pas, et le bilan dit combien de lignes ont été écartées. Le
    /// second exemplaire se saisit à la main, où le geste est délibéré.
    ///
    /// Tout passe dans UNE transaction : un import interrompu à mi-course
    /// laisserait sinon la moitié d'une étagère en base, sans qu'on sache
    /// laquelle.
    /// </summary>
    public Bilan Importer(IEnumerable<LigneInventaire> lignes)
    {
        // Toutes les lignes porteuses d'un ISBN. On compte SUR CELLES-CI, et non
        // sur les seules retenues : chaque ligne du fichier doit se retrouver
        // dans le bilan, créée ou écartée. Sinon « 150 créés » sur 300 lignes
        // scannées laisse chercher où sont passées les autres.
        var valides = lignes.Where(l => !string.IsNullOrWhiteSpace(l.Isbn)).ToList();
        if (valides.Count == 0) return new Bilan(0, 0);

        // Le même ISBN deux fois dans le fichier : on repasse devant l'étagère,
        // ou la caméra bipe deux fois. Une seule fiche, et l'écart apparaît dans
        // « déjà présents ».
        var aTraiter = valides.GroupBy(l => l.Isbn).Select(g => g.First());

        using var con = fabrique.Ouvrir();
        using var tx = con.BeginTransaction();

        var connus = con.Query<string>(
                "SELECT isbn FROM livres WHERE isbn IS NOT NULL", transaction: tx)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var crees = 0;

        foreach (var ligne in aTraiter)
        {
            if (!connus.Add(ligne.Isbn)) continue;

            // `cle_recherche` vide, comme le titre : elle en est le reflet, et
            // l'enrichissement la recalculera en même temps que lui. La laisser
            // à l'ISBN rendrait la fiche trouvable par un texte que personne ne
            // tape dans une barre de recherche.
            con.Execute("""
                INSERT INTO livres (isbn, titre, cle_recherche, rangement, source_notice, enrichi_le)
                VALUES (@Isbn, '', '', @Rangement, '', NULL)
                """, new { ligne.Isbn, ligne.Rangement }, tx);

            crees++;
        }

        tx.Commit();
        return new Bilan(crees, valides.Count - crees);
    }

    /// <summary>
    /// Les fiches qui attendent d'être complétées : celles qu'aucune source n'a
    /// encore été interrogée pour, et qui portent un ISBN.
    ///
    /// <c>enrichi_le</c> retient la dernière TENTATIVE, réussie ou non. C'est ce
    /// qui rend le traitement par lot reprenable d'un jour sur l'autre sans
    /// redemander aux sources ce qu'elles ont déjà refusé — même raisonnement
    /// que dans Lirevia, où les quotas gratuits se comptent en centaines
    /// d'appels quotidiens.
    ///
    /// Les fiches SANS ISBN sont écartées : aucune source ne saura répondre, et
    /// elles occuperaient la place de celles qu'on peut vraiment compléter.
    /// C'est le cas des ~30 % d'albums sans code-barres, qui se saisissent à la
    /// main.
    /// </summary>
    public List<Livre> ACompleter(int limite = 200)
    {
        using var con = fabrique.Ouvrir();

        return con.Query<Livre>("""
            SELECT id, isbn, titre, rangement
            FROM livres
            WHERE enrichi_le IS NULL
              AND isbn IS NOT NULL
              AND isbn <> ''
            ORDER BY id
            LIMIT @limite
            """, new { limite = Math.Clamp(limite, 1, 5000) }).AsList();
    }

    /// <summary>Combien de fiches restent à compléter.</summary>
    public int NombreACompleter()
    {
        using var con = fabrique.Ouvrir();

        return con.ExecuteScalar<int>("""
            SELECT COUNT(*) FROM livres
            WHERE enrichi_le IS NULL AND isbn IS NOT NULL AND isbn <> ''
            """);
    }

    /// <summary>
    /// Note qu'une fiche a été TENTÉE, qu'on ait trouvé ou non.
    ///
    /// Marquer même l'échec est ce qui fait avancer le traitement : sans cela,
    /// un ISBN qu'aucune source ne connaît serait redemandé à chaque passage, et
    /// le lot suivant repartirait sur les mêmes cent fiches introuvables.
    /// </summary>
    public void MarquerTentee(long id)
    {
        using var con = fabrique.Ouvrir();
        con.Execute("UPDATE livres SET enrichi_le = datetime('now') WHERE id = @id", new { id });
    }
}
