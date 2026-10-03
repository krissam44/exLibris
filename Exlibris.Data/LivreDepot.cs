using Dapper;
using Exlibris.Shared.Modeles;

namespace Exlibris.Data;

/// <summary>
/// Les ouvrages : création, modification, recherche.
///
/// <b>Méthodes SYNCHRONES, et c'est délibéré.</b> Les API <c>…Async</c> de
/// <c>Microsoft.Data.Sqlite</c> s'exécutent en réalité de façon synchrone : le
/// pilote n'a pas d'entrée/sortie asynchrone à offrir sur un fichier local.
/// Les employer donnerait une <i>fausse</i> asynchronie — du cérémonial, la
/// lisibilité en moins, et l'interface bloquée quand même. L'appelant qui
/// craint de figer la fenêtre enveloppe l'appel dans un <c>Task.Run</c>, ce qui
/// dit au moins la vérité sur ce qui se passe.
/// </summary>
public class LivreDepot(FabriqueConnexion fabrique)
{
    /// <summary>
    /// Les colonnes de lecture, jointures comprises. Écrites une fois : trois
    /// requêtes qui projettent chacune leur liste finiraient par rendre des
    /// objets différemment remplis selon le chemin emprunté.
    ///
    /// Les auteurs sont concaténés par <c>GROUP_CONCAT</c> plutôt que par une
    /// seconde requête : à l'affichage d'une liste, on veut une chaîne, pas des
    /// objets. La fiche détaillée, elle, les relit un par un avec leur rôle.
    /// </summary>
    private const string Colonnes = """
        SELECT l.id, l.isbn, l.titre,
               l.serie_id AS SerieId, l.numero_tome AS NumeroTome,
               l.editeur_id AS EditeurId, l.genre_id AS GenreId,
               l.classement_id AS ClassementId, l.proprietaire_id AS ProprietaireId,
               l.rangement, l.annee, l.nb_pages AS NbPages, l.format,
               l.resume, l.couverture,
               l.lu, l.date_lecture AS DateLecture,
               l.appreciation, l.commentaire,
               l.source_notice AS SourceNotice, l.enrichi_le AS EnrichiLe,
               l.cree_le AS CreeLe, l.modifie_le AS ModifieLe,
               COALESCE(s.nom, '') AS SerieNom,
               COALESCE(e.nom, '') AS EditeurNom,
               COALESCE(g.nom, '') AS GenreNom,
               COALESCE(c.nom, '') AS ClassementNom,
               COALESCE(p.nom, '') AS ProprietaireNom,
               COALESCE((SELECT GROUP_CONCAT(a.nom, ', ')
                         FROM livre_auteurs la
                         JOIN auteurs a ON a.id = la.auteur_id
                         WHERE la.livre_id = l.id), '') AS Auteurs
        FROM livres l
        LEFT JOIN series      s ON s.id = l.serie_id
        LEFT JOIN editeurs    e ON e.id = l.editeur_id
        LEFT JOIN genres  g ON g.id = l.genre_id
        LEFT JOIN classements   c ON c.id = l.classement_id
        LEFT JOIN proprietaires p ON p.id = l.proprietaire_id
        """;

    public long Creer(Livre livre)
    {
        using var con = fabrique.Ouvrir();
        return con.ExecuteScalar<long>($"""
            INSERT INTO livres (isbn, titre, cle_recherche, serie_id, numero_tome,
                                editeur_id, genre_id, classement_id, proprietaire_id,
                                rangement, annee, nb_pages,
                                format, resume, couverture, lu, date_lecture,
                                appreciation, commentaire, source_notice, enrichi_le)
            VALUES (@Isbn, @Titre, @Cle, @SerieId, @NumeroTome,
                    @EditeurId, @GenreId, @ClassementId, @ProprietaireId,
                    @Rangement, @Annee, @NbPages,
                    @Format, @Resume, @Couverture, @Lu, @DateLecture,
                    @Appreciation, @Commentaire, @SourceNotice, @EnrichiLe);
            SELECT last_insert_rowid();
            """, Parametres(livre));
    }

    public bool MettreAJour(Livre livre)
    {
        using var con = fabrique.Ouvrir();
        var lignes = con.Execute("""
            UPDATE livres SET
                isbn = @Isbn, titre = @Titre, cle_recherche = @Cle,
                serie_id = @SerieId, numero_tome = @NumeroTome,
                editeur_id = @EditeurId, genre_id = @GenreId,
                classement_id = @ClassementId, proprietaire_id = @ProprietaireId,
                rangement = @Rangement, annee = @Annee, nb_pages = @NbPages,
                format = @Format, resume = @Resume, couverture = @Couverture,
                lu = @Lu, date_lecture = @DateLecture,
                appreciation = @Appreciation, commentaire = @Commentaire,
                source_notice = @SourceNotice, enrichi_le = @EnrichiLe,
                modifie_le = datetime('now')
            WHERE id = @Id
            """, Parametres(livre, livre.Id));

        return lignes > 0;
    }

    public Livre? ParId(long id)
    {
        using var con = fabrique.Ouvrir();
        return con.QueryFirstOrDefault<Livre>($"{Colonnes} WHERE l.id = @id", new { id });
    }

    /// <summary>
    /// Recherche libre sur le titre, la série et les auteurs, plus l'ISBN exact.
    ///
    /// <b>Un <c>LIKE '%…%'</c> assumé.</b> Dans Lirevia, le pourcent de tête
    /// interdisait tout index et faisait balayer 540 000 fiches à chaque frappe —
    /// d'où l'index plein texte. Ici le fonds tient en quelques milliers de
    /// lignes : le balayage se compte en millisecondes, et un FTS5 serait de la
    /// complexité sans contrepartie.
    ///
    /// La saisie est repliée par <see cref="CleReferentiel.CleCollation"/> avant
    /// comparaison, faute de quoi « thorgal » ne trouverait pas « Thorgal » et
    /// « litterature » manquerait « Littérature » — SQLite ne repliant ni la
    /// casse au-delà de l'ASCII, ni les accents.
    /// </summary>
    public List<Livre> Rechercher(string? saisie = null, int limite = 500)
    {
        using var con = fabrique.Ouvrir();

        var propre = (saisie ?? string.Empty).Trim();
        if (propre.Length == 0)
            return con.Query<Livre>(
                $"{Colonnes} ORDER BY SerieNom, l.numero_tome, l.titre LIMIT @limite",
                new { limite = Borne(limite) }).AsList();

        // L'ISBN se compare tel quel, une fois normalisé : c'est le chemin de la
        // douchette, et il doit trouver du premier coup.
        var isbn = Isbn.Normaliser(propre);
        var motif = $"%{CleReferentiel.CleCollation(propre)}%";

        return con.Query<Livre>($"""
            {Colonnes}
            WHERE (@isbn <> '' AND l.isbn = @isbn)
               OR l.cle_recherche LIKE @motif
               OR COALESCE(s.cle_recherche, '') LIKE @motif
               OR EXISTS (SELECT 1 FROM livre_auteurs la
                          JOIN auteurs a ON a.id = la.auteur_id
                          WHERE la.livre_id = l.id AND a.cle_recherche LIKE @motif)
            ORDER BY SerieNom, l.numero_tome, l.titre
            LIMIT @limite
            """, new { isbn, motif, limite = Borne(limite) }).AsList();
    }

    /// <summary>
    /// Les livres portant déjà cet ISBN.
    ///
    /// Sert à SIGNALER un doublon à la saisie, jamais à le refuser : deux
    /// cadeaux identiques, cela arrive. C'est l'écran qui montre la fiche
    /// existante et demande s'il s'agit d'un second exemplaire ou d'une erreur
    /// de scan — la base, elle, ne s'y oppose pas.
    /// </summary>
    public List<Livre> ParIsbn(string? isbn)
    {
        var propre = Isbn.Normaliser(isbn);
        if (propre.Length == 0) return [];

        using var con = fabrique.Ouvrir();
        return con.Query<Livre>($"{Colonnes} WHERE l.isbn = @propre", new { propre }).AsList();
    }

    public bool Supprimer(long id)
    {
        using var con = fabrique.Ouvrir();
        // `livre_auteurs` part en cascade — à condition que les clés étrangères
        // soient actives, ce dont FabriqueConnexion se charge à chaque ouverture.
        return con.Execute("DELETE FROM livres WHERE id = @id", new { id }) > 0;
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  Auteurs d'un ouvrage
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Remplace la liste des auteurs d'un ouvrage. Table rase puis réécriture :
    /// un rapprochement ligne à ligne coûterait plus de code pour un gain nul
    /// sur trois ou quatre auteurs.
    /// </summary>
    public void DefinirAuteurs(long livreId, IEnumerable<(long AuteurId, string Role)> auteurs)
    {
        // ⚠️ MATÉRIALISÉ AVANT d'ouvrir la transaction, et c'est vital.
        //
        // Un appelant passe volontiers une séquence LINQ paresseuse — par
        // exemple des noms résolus par ReferentielDepot au fil de l'énumération.
        // Énumérée à l'intérieur de la transaction, elle ouvrirait une SECONDE
        // connexion en écriture pendant que celle-ci tient la base, et SQLite
        // rendrait « database is locked ». Le défaut ne s'est pas vu tant que
        // les appelants passaient des listes déjà constituées ; il est apparu au
        // premier traitement par lot.
        //
        // Le corriger ici plutôt que chez l'appelant est délibéré : aucun futur
        // appelant ne peut retomber dedans.
        var lignes = auteurs.ToList();

        using var con = fabrique.Ouvrir();
        using var tx = con.BeginTransaction();

        con.Execute("DELETE FROM livre_auteurs WHERE livre_id = @livreId", new { livreId }, tx);

        var rang = 0;
        foreach (var (auteurId, role) in lignes)
            con.Execute("""
                INSERT INTO livre_auteurs (livre_id, auteur_id, role, rang)
                VALUES (@livreId, @auteurId, @role, @rang)
                ON CONFLICT DO NOTHING
                """,
                new { livreId, auteurId, role, rang = rang++ }, tx);

        tx.Commit();
    }

    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Les paramètres d'écriture, y compris la clé de recherche et la conversion
    /// de l'ISBN vide en <c>NULL</c>.
    ///
    /// Centralisés ici parce que ces deux règles sont faciles à oublier dans
    /// l'une des deux requêtes, et qu'aucune des deux ne lèverait d'erreur : un
    /// ISBN vide passerait en chaîne vide, une clé oubliée rendrait le livre
    /// introuvable à la recherche.
    /// </summary>
    private static object Parametres(Livre livre, long? id = null) => new
    {
        Id = id ?? 0,
        Isbn = Isbn.Normaliser(livre.Isbn) is { Length: > 0 } propre ? propre : null,
        livre.Titre,
        Cle = CleReferentiel.CleCollation(livre.Titre),
        livre.SerieId,
        livre.NumeroTome,
        livre.EditeurId,
        livre.GenreId,
        livre.ClassementId,
        livre.ProprietaireId,
        livre.Rangement,
        livre.Annee,
        livre.NbPages,
        livre.Format,
        livre.Resume,
        livre.Couverture,
        livre.Lu,
        livre.DateLecture,
        livre.Appreciation,
        livre.Commentaire,
        livre.SourceNotice,
        livre.EnrichiLe,
    };

    /// <summary>
    /// Plafond de lecture. Ici et non seulement dans l'écran : le dépôt est le
    /// dernier endroit traversé par tous les appelants, présents et futurs.
    /// </summary>
    private static int Borne(int limite) => Math.Clamp(limite, 1, 20_000);
}
