using System.Reflection;

namespace Exlibris.Data;

/// <summary>
/// Le script de création de la base, lu depuis les ressources de cet assembly.
///
/// <b>Embarqué plutôt que copié à côté de l'exécutable</b>, pour deux raisons.
/// Il ne peut pas manquer au déploiement — une application livrée sans son
/// schéma échouerait au premier lancement, chez quelqu'un d'autre, sans qu'on
/// puisse rien y faire à distance. Et surtout, <b>les tests lisent exactement
/// celui que l'application applique</b> : un schéma dupliqué entre les deux
/// finit toujours par diverger, et la suite passe alors sur une structure qui
/// n'existe nulle part.
/// </summary>
public static class Schema
{
    private const string Ressource = "Exlibris.Data.Sql.schema.sql";

    /// <summary>
    /// Le script complet. Lève si la ressource manque — un échec bruyant au
    /// démarrage vaut infiniment mieux qu'une base créée à moitié, dont on ne
    /// s'apercevrait qu'à la première requête sur une table absente.
    /// </summary>
    public static string Sql()
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var flux = assembly.GetManifestResourceStream(Ressource)
            ?? throw new InvalidOperationException(
                $"Ressource « {Ressource} » introuvable dans {assembly.GetName().Name}. "
                + "Vérifiez la balise <EmbeddedResource> du csproj : le chemin de la "
                + "ressource suit celui du fichier, points à la place des séparateurs.");

        using var lecteur = new StreamReader(flux);
        return lecteur.ReadToEnd();
    }

    /// <summary>
    /// Les migrations à jouer <b>AVANT</b> le script de schéma.
    ///
    /// ⚠️ CETTE PHASE EXISTE POUR LES RENOMMAGES, et l'ordre n'est pas une
    /// préférence de style. <c>CreerSiNecessaire</c> est en
    /// « CREATE TABLE IF NOT EXISTS » : sur une base d'avant le renommage, il ne
    /// voit pas de table « genres » et en crée une, VIDE. Le renommage joué
    /// ensuite échouerait — la table de destination existe déjà — et les genres
    /// resteraient prisonniers de l'ancienne table pendant que l'application
    /// interrogerait la nouvelle. Aucune erreur, aucun message : simplement tous
    /// les genres vides.
    ///
    /// La règle générale : <b>renommer précède créer, ajouter suit.</b>
    ///
    /// Défaut trouvé le 30/08/2026 par
    /// <c>GenreTests.Une_base_au_VOCABULAIRE_d_avant_se_renomme_sans_rien_perdre</c>,
    /// qui est le seul test à parcourir ce chemin.
    /// </summary>
    public static IReadOnlyList<string> MigrationsPrealables() =>
    [
        // ----- Renommage « catégorie » → « genre », 30/08/2026.
        //
        // Deux champs portaient le même mot, distingués par un qualificatif :
        // « Catégorie éditeur » et « Ma catégorie ». Un qualificatif est une
        // distinction faible. « Genre » est en outre le mot du métier — celui
        // d'une quatrième de couverture et d'un rayon de librairie.
        //
        // ⚠️ CES QUATRE LIGNES DOIVENT PRÉCÉDER les suivantes. Sur une base
        // d'avant le renommage, un ADD COLUMN qui référencerait `genres`
        // échouerait — la table s'appelle encore `categories` — et la base
        // resterait muette pendant que l'application interrogerait des noms
        // qui n'existent pas.
        "ALTER TABLE categories RENAME TO genres",
        "ALTER TABLE livres RENAME COLUMN categorie_id TO genre_id",

        // Les alias de fusion portent le nom de la table VISÉE, en clair.
        // Oubliés ici, les rapprochements décidés à la main pointeraient vers
        // un référentiel « categories » que plus personne ne consulte : les
        // libellés fusionnés reviendraient à la première notice.
        "UPDATE alias_referentiel SET referentiel = 'genres' WHERE referentiel = 'categories'",

        // SQLite reporte un renommage de colonne sur ses index, mais garde
        // leur ancien nom. Inoffensif, et trompeur à la lecture du schéma.
        "DROP INDEX IF EXISTS idx_livres_categorie",

    ];

    /// <summary>
    /// Les ajouts de colonnes sur des tables DÉJÀ créées.
    ///
    /// <c>CREATE TABLE IF NOT EXISTS</c> ne sert que la base neuve : sur une
    /// base existante la table est là, l'instruction ne fait rien, et la colonne
    /// nouvelle manque. Sans ce complément, ajouter un champ marcherait chez le
    /// développeur — qui efface sa base — et échouerait chez l'utilisateur, qui
    /// a ses deux mille livres dedans. C'est le défaut qu'on ne voit jamais
    /// avant la mise en service.
    ///
    /// Chaque instruction est jouée à part et son échec ignoré : SQLite refuse
    /// une colonne déjà présente, ce qui est le cas normal dès le deuxième
    /// démarrage. Même motif idempotent que DomoMud.
    ///
    /// ⚠️ SQLite impose qu'une colonne ajoutée avec <c>REFERENCES</c> ait
    /// <c>NULL</c> pour valeur par défaut. C'est le cas ici, et c'est de toute
    /// façon ce qu'on veut : les livres déjà saisis n'ont pas de genre.
    /// </summary>
    /// ⚠️ L'INDEX D'UNE COLONNE AJOUTÉE APPARTIENT ICI, PAS AU SCHÉMA.
    /// C'est le piège qui a planté le démarrage sur la base réelle : le schéma
    /// s'exécute AVANT les migrations, donc un
    /// <c>CREATE INDEX … ON livres(genre_id)</c> resté là-bas s'exécutait
    /// alors que la colonne n'existait pas encore, et faisait échouer tout le
    /// script — base neuve indemne, base de l'utilisateur inutilisable. La règle
    /// à retenir : le schéma ne connaît que ce qu'il crée lui-même.
    public static IReadOnlyList<string> Migrations() =>
    [
        "ALTER TABLE livres ADD COLUMN genre_id INTEGER NULL REFERENCES genres(id) ON DELETE SET NULL",
        "CREATE INDEX IF NOT EXISTS idx_livres_genre ON livres(genre_id)",
        "ALTER TABLE livres ADD COLUMN proprietaire_id INTEGER NULL REFERENCES proprietaires(id) ON DELETE SET NULL",
        "CREATE INDEX IF NOT EXISTS idx_livres_proprietaire ON livres(proprietaire_id)",
    ];
}
