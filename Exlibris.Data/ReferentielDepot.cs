using Dapper;
using Exlibris.Shared.Modeles;

namespace Exlibris.Data;

/// <summary>
/// Éditeurs, auteurs, séries, classements : les quatre listes que la saisie
/// alimente au fil de l'eau.
///
/// <b>Chaque table porte une `cle_recherche` calculée en C#</b>, et c'est elle
/// qui porte l'unicité — jamais le libellé. SQLite ne replie pas les accents :
/// laissé à lui seul, il créerait « Littérature » et « Litterature » comme deux
/// rayons distincts, et personne ne s'en apercevrait avant d'en avoir trente.
///
/// <b>Aucun index en mémoire</b>, contrairement à Lirevia. Là-bas, un cache par
/// requête HTTP évitait des allers-retours réseau sur un import de 500 000
/// notices — et c'est ce cache qui, gardé trop longtemps par le banc d'essai,
/// servait des identifiants de lignes supprimées. Ici la base est un fichier
/// local et la recherche est un accès indexé : le cache coûterait sa propre
/// invalidation pour un gain invisible.
/// </summary>
public class ReferentielDepot(FabriqueConnexion fabrique)
{
    public long? ResoudreEditeur(string? nom) =>
        ResoudreOuCreer("editeurs", nom, LongueurNom);

    /// <summary>
    /// La clé de rapprochement d'un libellé, telle que le référentiel la
    /// définit. Les éditeurs replient la ponctuation et le préfixe
    /// « Éditions » ; les autres s'en tiennent à la casse et aux accents.
    /// </summary>
    private static string Cle(string table, string nom) =>
        Referentiel.Par(table)?.Cle(nom) ?? CleReferentiel.CleCollation(nom);

    public long? ResoudreAuteur(string? nom) =>
        ResoudreOuCreer("auteurs", nom, LongueurNom);

    public long? ResoudreSerie(string? nom) =>
        ResoudreOuCreer("series", nom, LongueurNom);

    public long? ResoudreClassement(string? nom) =>
        ResoudreOuCreer("classements", nom, LongueurNom);

    public long? ResoudreGenre(string? nom) =>
        ResoudreOuCreer("genres", nom, LongueurNom);

    public long? ResoudreProprietaire(string? nom) =>
        ResoudreOuCreer("proprietaires", nom, LongueurNom);

    /// <summary>
    /// Largeur retenue pour un libellé de référentiel. SQLite ne fait pas
    /// respecter les longueurs — un VARCHAR(n) y accepte tout — mais un nom
    /// d'éditeur de trois cents caractères rendrait les listes déroulantes
    /// inutilisables. La coupe se fait au dernier mot, comme dans Lirevia.
    /// </summary>
    private const int LongueurNom = 150;

    /// <summary>
    /// Renvoie l'identifiant du libellé, le crée s'il n'existe pas encore.
    /// <c>null</c> pour une saisie vide : un livre peut n'avoir ni éditeur ni
    /// série.
    ///
    /// Le nom de table est interpolé, ce qui serait inacceptable s'il venait de
    /// l'extérieur — il ne vient que des quatre méthodes ci-dessus, aucune
    /// n'étant paramétrable par l'appelant.
    /// </summary>
    private long? ResoudreOuCreer(string table, string? nom, int longueurMax)
    {
        var propre = CleReferentiel.Tronquer(nom, longueurMax);
        if (propre.Length == 0) return null;

        var cle = Cle(table, propre);

        using var con = fabrique.Ouvrir();

        // ----- 0. Un libellé déjà FUSIONNÉ par le passé.
        //
        // C'est ce qui fait tenir une fusion. Sans cette lecture, la notice
        // suivante recréerait « L.G.F - Le livre de poche » à côté du « Livre
        // de poche » qu'on venait d'y verser, et le ménage serait à refaire
        // tous les mois.
        //
        // La jointure sur la table cible n'est pas décorative : un alias dont
        // la cible a été supprimée depuis ne doit pas rendre un identifiant
        // mort, que l'appelant écrirait dans `livres` sans que rien ne lui
        // résiste — les clés étrangères n'empêchent que l'inverse.
        var alias = con.ExecuteScalar<long?>($"""
            SELECT a.cible_id FROM alias_referentiel a
            JOIN {table} t ON t.id = a.cible_id
            WHERE a.referentiel = @table AND a.cle_recherche = @cle
            """, new { table, cle });

        if (alias is not null) return alias;

        // ----- 1. Lecture d'abord, et c'est le cas COURANT.
        //
        // ⚠️ Cette lecture n'est pas une optimisation : elle empêche de BRÛLER
        // un identifiant à chaque enregistrement. Mesuré le 29/08/2026 sur
        // SQLite 3.49 : `INSERT … ON CONFLICT DO NOTHING` alloue son identifiant
        // AVANT de découvrir le conflit, et `AUTOINCREMENT` rend cette
        // allocation définitive en la gravant dans `sqlite_sequence`. Onze
        // enregistrements d'une fiche dont le genre existe déjà donnaient
        // 1 puis 12 — d'où les suites 1, 5, 12, 16, 27, 38 observées dans les
        // référentiels.
        //
        // Le trou lui-même est sans conséquence — un identifiant de substitution
        // ne se montre à personne. Ce qui est fautif, c'est l'écriture inutile
        // qui le creuse : chaque sauvegarde tentait cinq INSERT ne créant rien.
        var existant = con.ExecuteScalar<long?>(
            $"SELECT id FROM {table} WHERE cle_recherche = @cle", new { cle });

        if (existant is not null) return existant;

        // ----- 2. Absent : on crée, sans lâcher la garde contre la course.
        //
        // `ON CONFLICT DO NOTHING` reste, et la relecture derrière aussi : entre
        // le SELECT ci-dessus et cet INSERT, un import lancé en parallèle peut
        // avoir créé la ligne. Ce chemin ne se prend qu'à la PREMIÈRE apparition
        // d'un libellé, où l'identifiant consommé l'est pour de bon.
        con.Execute(
            $"INSERT INTO {table} (nom, cle_recherche) VALUES (@propre, @cle) ON CONFLICT DO NOTHING",
            new { propre, cle });

        return con.ExecuteScalar<long>(
            $"SELECT id FROM {table} WHERE cle_recherche = @cle", new { cle });
    }

    /// <summary>
    /// Recalcule les clés des éditeurs avec la clé qui leur est propre, et
    /// rapproche ce qui se rejoint.
    ///
    /// <b>Pourquoi c'est obligatoire et pas seulement souhaitable.</b> Les
    /// éditeurs sont passés de <c>CleCollation</c> (casse et accents) à
    /// <c>CleReferentiel.Editeur</c> (ponctuation et préfixe « Éditions » en
    /// plus). Les clés déjà en base ont été calculées avec l'ancienne : sans ce
    /// rattrapage, « L.G.F - Le livre de poche » chercherait désormais
    /// « l g f le livre de poche », ne trouverait pas « l.g.f - le livre de
    /// poche » qui y dort, et créerait un doublon de plus à chaque notice.
    /// Le correctif aurait aggravé ce qu'il prétendait corriger.
    ///
    /// Deux temps, et l'ordre est impératif : d'abord fusionner les libellés que
    /// la nouvelle clé réunit, ensuite seulement réécrire les clés. L'inverse
    /// heurterait la contrainte d'unicité au premier doublon.
    ///
    /// Le survivant d'une fusion automatique est celui qui porte le PLUS
    /// d'ouvrages, et à égalité le plus ancien : c'est le libellé qu'on verra le
    /// plus souvent, donc celui dont la disparition surprendrait le plus.
    ///
    /// Idempotent : au deuxième lancement, aucune clé ne change et rien n'est
    /// écrit.
    /// </summary>
    public int RecalculerClesEditeurs()
    {
        var editeur = Referentiel.Par("editeurs")!;

        List<(long Id, string Nom, string Ancienne)> lignes;
        using (var con = fabrique.Ouvrir())
            lignes = con.Query<(long, string, string)>(
                "SELECT id, nom, cle_recherche FROM editeurs").AsList();

        var nouvelles = lignes
            .Select(l => (l.Id, l.Ancienne, Nouvelle: editeur.Cle(l.Nom)))
            .Where(l => l.Nouvelle.Length > 0)
            .ToList();

        if (nouvelles.All(l => l.Ancienne == l.Nouvelle)) return 0;

        var usages = Lister("editeurs").ToDictionary(e => e.Id, e => e.NbOuvrages);
        var fusionnes = 0;

        foreach (var groupe in nouvelles.GroupBy(l => l.Nouvelle).Where(g => g.Count() > 1))
        {
            var ordonnes = groupe
                .OrderByDescending(l => usages.GetValueOrDefault(l.Id))
                .ThenBy(l => l.Id)
                .ToList();

            Fusionner("editeurs", ordonnes[0].Id, ordonnes.Skip(1).Select(l => l.Id));
            fusionnes += ordonnes.Count - 1;
        }

        // Les clés, une fois les collisions résolues. Relu depuis la base : les
        // fusions ci-dessus ont supprimé des lignes, et réécrire une clé sur un
        // identifiant disparu ne lèverait rien tout en ne faisant rien.
        using (var con = fabrique.Ouvrir())
        {
            foreach (var l in nouvelles.Where(l => l.Ancienne != l.Nouvelle))
                con.Execute(
                    "UPDATE editeurs SET cle_recherche = @Nouvelle WHERE id = @Id",
                    new { l.Nouvelle, l.Id });
        }

        return fusionnes;
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  Entretien : lister et fusionner
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Les entrées d'un référentiel, avec le nombre d'ouvrages qui s'y
    /// rattachent.
    ///
    /// Le décompte est là pour la décision : devant quatre « Livre de poche »,
    /// on garde celui qui porte deux cents ouvrages, pas celui qui en porte un.
    /// Trié par nom et non par usage — on cherche des doublons, donc on veut les
    /// libellés voisins l'un sous l'autre.
    /// </summary>
    public List<EntreeReferentiel> Lister(string table)
    {
        var r = Referentiel.Par(table)
            ?? throw new ArgumentException($"Référentiel inconnu : {table}", nameof(table));

        using var con = fabrique.Ouvrir();

        return con.Query<EntreeReferentiel>($"""
            SELECT t.id, t.nom,
                   (SELECT COUNT(*) FROM {r.TablePorteuse} p
                     WHERE p.{r.ColonneEtrangere} = t.id) AS NbOuvrages
            FROM {r.Table} t
            ORDER BY t.nom
            """).AsList();
    }

    /// <summary>
    /// Verse plusieurs libellés dans un seul, et RETIENT la décision.
    ///
    /// <b>Trois choses en une transaction</b>, et l'ordre importe :
    ///
    /// 1. les ouvrages passent au survivant ;
    /// 2. les clés absorbées deviennent des alias vers lui — c'est ce qui
    ///    empêche la prochaine notice de recréer ce qu'on vient d'effacer ;
    /// 3. les lignes absorbées disparaissent.
    ///
    /// Inverser 1 et 3 laisserait les ouvrages rattachés à un identifiant
    /// supprimé : les clés étrangères sont en <c>ON DELETE SET NULL</c>, donc
    /// les livres perdraient leur éditeur en silence au lieu de changer de main.
    ///
    /// Renvoie le nombre d'ouvrages déplacés.
    /// </summary>
    public int Fusionner(string table, long survivantId, IEnumerable<long> absorbes)
    {
        var r = Referentiel.Par(table)
            ?? throw new ArgumentException($"Référentiel inconnu : {table}", nameof(table));

        // Le survivant retiré de la liste : le passer en absorbé le supprimerait
        // après lui avoir réaffecté ses propres ouvrages.
        var cibles = absorbes.Where(id => id != survivantId).Distinct().ToList();
        if (cibles.Count == 0) return 0;

        using var con = fabrique.Ouvrir();
        using var tx = con.BeginTransaction();

        // Les clés à mémoriser, lues AVANT la suppression.
        var cles = con.Query<string>(
            $"SELECT cle_recherche FROM {r.Table} WHERE id IN @cibles",
            new { cibles }, tx).AsList();

        // ⚠️ `UPDATE OR IGNORE` et non `UPDATE`. Les auteurs passent par une
        // table de liaison dont la clé primaire est (livre_id, auteur_id, role) :
        // fusionner deux auteurs présents sur le MÊME ouvrage produirait une
        // ligne en double, et un UPDATE nu ferait échouer toute la fusion sur ce
        // seul cas. Ignorée, la ligne surnuméraire est simplement laissée telle
        // quelle, puis emportée par la suppression du référentiel absorbé —
        // qui est en cascade sur cette table.
        var deplaces = con.Execute($"""
            UPDATE OR IGNORE {r.TablePorteuse}
               SET {r.ColonneEtrangere} = @survivantId
             WHERE {r.ColonneEtrangere} IN @cibles
            """, new { survivantId, cibles }, tx);

        foreach (var cle in cles)
            con.Execute("""
                INSERT INTO alias_referentiel (referentiel, cle_recherche, cible_id)
                VALUES (@table, @cle, @survivantId)
                ON CONFLICT(referentiel, cle_recherche) DO UPDATE SET cible_id = excluded.cible_id
                """, new { table = r.Table, cle, survivantId }, tx);

        // Un alias qui pointait vers un absorbé suit le mouvement : sans cela,
        // une deuxième fusion ferait pointer d'anciens alias vers une ligne
        // disparue, et la résolution les ignorerait — la première décision
        // serait silencieusement perdue.
        con.Execute("""
            UPDATE alias_referentiel SET cible_id = @survivantId
             WHERE referentiel = @table AND cible_id IN @cibles
            """, new { table = r.Table, survivantId, cibles }, tx);

        con.Execute($"DELETE FROM {r.Table} WHERE id IN @cibles", new { cibles }, tx);

        tx.Commit();
        return deplaces;
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  Lectures, pour les listes d'autocomplétion de la saisie manuelle
    // -----------------------------------------------------------------------------------------------------------------

    public List<Editeur> Editeurs()
    {
        using var con = fabrique.Ouvrir();
        return con.Query<Editeur>("SELECT id, nom FROM editeurs ORDER BY nom").AsList();
    }

    public List<Auteur> Auteurs()
    {
        using var con = fabrique.Ouvrir();
        return con.Query<Auteur>("SELECT id, nom FROM auteurs ORDER BY nom").AsList();
    }

    /// <summary>
    /// Les classements, dans l'ordre CHOISI puis alphabétique. Le rang à égalité
    /// vaut 0 pour tous tant que personne ne l'a réglé : le repli alphabétique
    /// évite un ordre d'insertion apparemment aléatoire.
    /// </summary>
    public List<Classement> Classements()
    {
        using var con = fabrique.Ouvrir();
        return con.Query<Classement>(
            "SELECT id, nom, rang FROM classements ORDER BY rang, nom").AsList();
    }

    /// <summary>
    /// Les séries, avec ce qu'il faut pour repérer les incomplètes : les rangs
    /// possédés, leur nombre, et le plus haut d'entre eux.
    ///
    /// Le calcul des tomes MANQUANTS se fait en C# sur ce résultat
    /// (<see cref="TomesManquants"/>). À quelques centaines de séries c'est
    /// instantané, et l'exprimer en SQL rendrait la requête illisible pour rien.
    ///
    /// ⚠️ DEUX REQUÊTES, sur UNE seule connexion. La liste des rangs ne peut pas
    /// venir de la première : elle est agrégée par série, et un GROUP BY ne rend
    /// pas les valeurs qu'il a repliées. Les demander série par série ferait une
    /// requête par ligne — le défaut classique — alors qu'un seul balayage de
    /// `livres` suffit, la répartition se faisant en mémoire.
    /// </summary>
    public List<Serie> Series()
    {
        using var con = fabrique.Ouvrir();

        var series = con.Query<Serie>("""
            SELECT s.id, s.nom, s.tomes_annonces AS TomesAnnonces,
                   s.terminee AS Terminee, s.note AS Note,
                   COUNT(l.id)         AS NbPossedes,
                   MAX(l.numero_tome)  AS TomeMax
            FROM series s
            LEFT JOIN livres l ON l.serie_id = s.id AND l.numero_tome IS NOT NULL
            GROUP BY s.id, s.nom, s.tomes_annonces, s.terminee, s.note
            ORDER BY s.nom
            """).AsList();

        var rangs = con.Query<TomePossede>("""
            SELECT serie_id AS SerieId, numero_tome AS Numero
            FROM livres
            WHERE serie_id IS NOT NULL AND numero_tome IS NOT NULL
            """).ToLookup(t => t.SerieId);

        foreach (var serie in series)
            serie.TomesPossedes = rangs[serie.Id].Select(t => t.Numero).ToList();

        return series;
    }

    /// <summary>
    /// Le couple (série, rang) tel que Dapper le rend. Une classe nommée plutôt
    /// qu'un tuple : Dapper mappe par nom de propriété, et un ValueTuple n'en a
    /// pas — ses champs s'appellent Item1 et Item2.
    /// </summary>
    private sealed class TomePossede
    {
        public long SerieId { get; set; }
        public int Numero { get; set; }
    }

    /// <summary>
    /// Enregistre ce qu'AUCUNE source ne donne : le nombre de tomes annoncés, le
    /// fait que la série soit terminée, et la note personnelle.
    ///
    /// Le nom n'est volontairement PAS modifiable ici. Un renommage de série
    /// touche à sa clé de recherche et au rapprochement des libellés — c'est le
    /// travail de <see cref="Fusionner"/>, qui sait absorber « Thorgal » et
    /// « Thorgal (Le Lombard) » sans laisser d'orphelins. Le faire aussi ici
    /// donnerait deux chemins pour la même opération, dont un seul correct.
    /// </summary>
    public bool MettreAJourSerie(long id, int? tomesAnnonces, bool terminee, string note)
    {
        // Zéro et les négatifs valent « je ne sais pas », et non « série de zéro
        // tome » : sans ce repli, un 0 saisi par mégarde ne changerait rien à
        // l'affichage (la borne prend le plus grand des deux) mais rendrait
        // impossible de distinguer l'ignorance de la saisie.
        var annonces = tomesAnnonces is > 0 ? tomesAnnonces : null;

        using var con = fabrique.Ouvrir();
        return con.Execute("""
            UPDATE series
            SET tomes_annonces = @annonces, terminee = @terminee, note = @note
            WHERE id = @id
            """, new { id, annonces, terminee, note = note ?? string.Empty }) > 0;
    }

    /// <summary>
    /// Les genres, dans l'ordre alphabétique. Contrairement aux classements, ils
    /// n'ont pas de rang choisi : c'est une nomenclature, pas l'ordre de vos
    /// étagères.
    /// </summary>
    public List<Genre> Genres()
    {
        using var con = fabrique.Ouvrir();
        return con.Query<Genre>("SELECT id, nom FROM genres ORDER BY nom").AsList();
    }

    /// <summary>
    /// Pose la nomenclature de départ, <b>et seulement si la table est vide</b>.
    ///
    /// La condition est la règle importante. Semer à chaque démarrage ferait
    /// revenir un genre supprimé au lancement suivant — l'utilisateur corrigerait
    /// une fois, deux fois, puis conclurait que l'application ne l'écoute pas.
    /// Une liste vide ne signifie « base neuve » qu'au premier lancement ; si
    /// vous videz la liste volontairement, elle se repeuplera une fois. C'est le
    /// compromis assumé d'un amorçage sans table de suivi.
    ///
    /// L'amorçage se fait en C# et non par des INSERT dans le schéma, pour que
    /// la clé de rapprochement soit calculée par <see cref="CleReferentiel"/> —
    /// la même fonction que la saisie. Des clés écrites à la main dans le SQL
    /// divergeraient au premier accent, et « Science-fiction » tapé au clavier
    /// créerait un doublon du genre semé.
    /// </summary>
    public void SemerGenresSiVide()
    {
        using (var con = fabrique.Ouvrir())
        {
            if (con.ExecuteScalar<long>("SELECT COUNT(*) FROM genres") > 0) return;
        }

        // Volontairement COURTE. Une nomenclature de cinquante entrées se
        // parcourt mal dans une liste déroulante et pousse à hésiter entre deux
        // cases voisines ; ce qui manque s'ajoute en le tapant, la liste étant
        // ouverte à la saisie.
        foreach (var genre in new[]
        {
            "Bande dessinée", "Manga", "Comics", "Roman", "Policier", "Thriller",
            "Science-fiction", "Fantasy", "Fantastique", "Jeunesse", "Album illustré",
            "Biographie", "Histoire", "Essai", "Documentaire", "Art", "Poésie",
            "Théâtre", "Cuisine", "Voyage", "Pratique",
        })
            ResoudreGenre(genre);
    }

    /// <summary>
    /// Les propriétaires connus. Vide tant que personne n'en a saisi un, ce qui
    /// est le cas normal : la liste se remplit en tapant, comme les autres.
    /// </summary>
    public List<Proprietaire> Proprietaires()
    {
        using var con = fabrique.Ouvrir();
        return con.Query<Proprietaire>("SELECT id, nom FROM proprietaires ORDER BY nom").AsList();
    }

    /// <summary>
    /// Les rangements déjà employés, pour la suggestion de saisie. Aucun
    /// référentiel à maintenir : « Salle 1, étagère B » multiplié par deux cents
    /// rayons ferait de l'écran de paramétrage une corvée.
    /// </summary>
    public List<string> Rangements()
    {
        using var con = fabrique.Ouvrir();
        return con.Query<string>("""
            SELECT DISTINCT rangement FROM livres
            WHERE rangement <> '' ORDER BY rangement
            """).AsList();
    }
}
