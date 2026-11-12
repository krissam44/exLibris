using Dapper;
using Exlibris.Shared.Modeles;

namespace Exlibris.Data;

/// <summary>
/// Les chiffres de l'accueil, au-delà des quatre compteurs d'AccueilViewModel
/// (12/11/2026) : la part de chaque propriétaire, la lecture, les fiches à
/// reprendre, et surtout les IDÉES CADEAUX — les tomes qui manquent à chacun
/// dans les séries qu'il a commencées.
/// </summary>
public class TableauDeBordDepot(FabriqueConnexion fabrique)
{
    /// <summary>
    /// Les ouvrages d'un propriétaire. <c>Id</c> vaut <see cref="Proprietaire.Aucun"/>
    /// pour la ligne « sans propriétaire ».
    /// </summary>
    /// <remarks>
    /// ⚠️ Des CLASSES à propriétés, pas des records positionnels. Dapper
    /// construit un record par son constructeur, dont les types doivent égaler
    /// EXACTEMENT ceux des colonnes ; or SQLite ne type pas une colonne
    /// calculée (COUNT, SUM) — Int64 sur une ligne, byte[] sur un résultat
    /// vide — et la lecture échouait (constaté le 12/11/2026). Par propriétés,
    /// Dapper convertit valeur par valeur.
    /// </remarks>
    public sealed class PartProprietaire
    {
        public long Id { get; set; }
        public string Nom { get; set; } = string.Empty;
        public int NbOuvrages { get; set; }
        public int NbLus { get; set; }
    }

    /// <summary>Ce qui manque aux fiches : des chiffres pour décider d'une séance de reprise.</summary>
    public sealed class FichesAReprendre
    {
        public int SansCouverture { get; set; }
        public int SansAuteur { get; set; }
        public int SansGenre { get; set; }
        public int SansResume { get; set; }
    }

    /// <summary>Une série commencée, et les tomes qui y manquent encore.</summary>
    public record IdeeCadeau(string Serie, IReadOnlyList<int> Manquants, int Possedes, bool Terminee)
    {
        /// <summary>« 3, 4, 12 à 24 » — la forme de l'écran Séries.</summary>
        public string Resume => TomesManquants.Resumer(Manquants);
    }

    /// <summary>
    /// Chaque propriétaire avec son nombre d'ouvrages, plus une ligne « Sans
    /// propriétaire » quand il en reste. Un propriétaire sans ouvrage figure
    /// aussi : on vient de le créer, l'import de ses BD est à faire.
    /// </summary>
    public List<PartProprietaire> ParProprietaire()
    {
        using var con = fabrique.Ouvrir();

        var parts = con.Query<PartProprietaire>("""
            SELECT p.id AS Id, p.nom AS Nom,
                   COUNT(l.id) AS NbOuvrages,
                   COALESCE(SUM(l.lu), 0) AS NbLus
            FROM proprietaires p
            LEFT JOIN livres l ON l.proprietaire_id = p.id
            GROUP BY p.id, p.nom
            ORDER BY p.nom
            """).AsList();

        var sans = con.QueryFirst<(int Nb, int Lus)>(
            "SELECT COUNT(*), COALESCE(SUM(lu), 0) FROM livres WHERE proprietaire_id IS NULL");
        if (sans.Nb > 0)
            parts.Add(new PartProprietaire
            {
                Id = Proprietaire.Aucun, Nom = "Sans propriétaire", NbOuvrages = sans.Nb, NbLus = sans.Lus,
            });

        return parts;
    }

    /// <summary>Le nombre d'ouvrages lus, sur toute la bibliothèque.</summary>
    public int NombreLus()
    {
        using var con = fabrique.Ouvrir();
        return con.ExecuteScalar<int>("SELECT COUNT(*) FROM livres WHERE lu = 1");
    }

    /// <summary>
    /// Ce qui manque aux fiches DÉJÀ complétées. Celles qui attendent encore
    /// les sources en ligne (sans titre) ne comptent pas : il leur manque tout,
    /// et l'étape 2 de l'inventaire s'en charge — les compter ici noierait le
    /// vrai travail de reprise sous l'attente.
    /// </summary>
    public FichesAReprendre AReprendre()
    {
        using var con = fabrique.Ouvrir();
        // COALESCE : sur une bibliothèque vide, SUM rend NULL et non 0.
        return con.QueryFirst<FichesAReprendre>("""
            SELECT COALESCE(SUM(couverture = ''), 0)                       AS SansCouverture,
                   COALESCE(SUM(NOT EXISTS (SELECT 1 FROM livre_auteurs la
                                            WHERE la.livre_id = l.id)), 0) AS SansAuteur,
                   COALESCE(SUM(genre_id IS NULL), 0)                      AS SansGenre,
                   COALESCE(SUM(resume = ''), 0)                           AS SansResume
            FROM livres l
            WHERE titre <> ''
            """);
    }

    /// <summary>
    /// Les idées cadeaux d'un propriétaire : pour chaque série où il possède au
    /// moins un tome NUMÉROTÉ, les tomes qui lui manquent — trous intérieurs, et
    /// la fin quand le nombre de tomes de la série est connu.
    ///
    /// <b>Calculé sur SES ouvrages seulement</b>, et c'est tout l'intérêt : la
    /// série est commune, les étagères ne le sont pas. Le tome 12 que possède le
    /// fils manque toujours au petit-fils. <see cref="Proprietaire.Aucun"/>
    /// vise les ouvrages sans propriétaire, <c>null</c> toute la bibliothèque
    /// (propriétaires non distingués).
    ///
    /// Une série terminée sans manque n'apparaît pas ; les séries les plus
    /// lacunaires viennent en tête, puis l'ordre alphabétique.
    /// </summary>
    public List<IdeeCadeau> IdeesCadeaux(long? proprietaireId)
    {
        using var con = fabrique.Ouvrir();

        var lignes = con.Query<(long SerieId, string Nom, int? Annonces, bool Terminee, int Tome)>("""
            SELECT s.id, s.nom, s.tomes_annonces, s.terminee, l.numero_tome
            FROM livres l
            JOIN series s ON s.id = l.serie_id
            WHERE l.numero_tome IS NOT NULL
              AND (@proprietaireId IS NULL
                   OR (@proprietaireId = 0 AND l.proprietaire_id IS NULL)
                   OR l.proprietaire_id = @proprietaireId)
            """, new { proprietaireId });

        return lignes
            .GroupBy(l => (l.SerieId, l.Nom, l.Annonces, l.Terminee))
            .Select(g =>
            {
                var possedes = g.Select(l => l.Tome).Distinct().ToList();
                return new IdeeCadeau(g.Key.Nom, TomesManquants.Calculer(possedes, g.Key.Annonces),
                    possedes.Count, g.Key.Terminee);
            })
            .Where(i => i.Manquants.Count > 0)
            .OrderByDescending(i => i.Manquants.Count)
            .ThenBy(i => i.Serie, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
