using Microsoft.Data.Sqlite;

namespace Exlibris.Data;

/// <summary>
/// Ouvre les connexions à la base. Un seul endroit, pour une seule raison :
/// <b>garantir que chaque connexion active les clés étrangères</b>.
///
/// SQLite les désactive par défaut, et le fait sans rien dire — une contrainte
/// déclarée dans le schéma mais jamais appliquée. Un livre rattaché à une série
/// supprimée passerait sans un mot, et la base se dégraderait à bas bruit. Le
/// <c>PRAGMA</c> du schéma ne vaut que pour la connexion qui l'a exécuté : il
/// doit donc être rejoué à chaque ouverture, et c'est exactement ce que cette
/// classe rend impossible d'oublier.
/// </summary>
/// <param name="cheminBase">Chemin du fichier <c>.db</c>.</param>
/// <param name="regroupement">
/// Le regroupement de connexions. <c>true</c> en production : c'est lui qui rend
/// l'ouverture négligeable, et l'application est de toute façon seule sur son
/// fichier.
///
/// ⚠️ <b>Les tests le désactivent</b>, pour une raison précise. Un banc d'essai
/// doit effacer sa base à la fin, or le regroupement garde le fichier ouvert
/// après la dernière libération et la suppression échoue sous Windows. La parade
/// évidente — <c>SqliteConnection.ClearAllPools()</c> — est PROCESSUS-GLOBALE :
/// elle libère aussi les connexions des autres classes de test, que xUnit
/// exécute en parallèle, lesquelles tombent alors sur « Cannot access a disposed
/// object : SQLitePCL.sqlite3 ». Défaut constaté le 30/08/2026, visible au seul
/// premier passage après reconstruction — c'est-à-dire un échec intermittent, la
/// variété la plus coûteuse à diagnostiquer.
///
/// Sans regroupement, chaque connexion libérée ferme réellement le fichier :
/// plus rien à vider, donc plus rien à casser chez le voisin.
/// </param>
public sealed class FabriqueConnexion(string cheminBase, bool regroupement = true)
{
    /// <summary>Chemin du fichier `.db`, utile aux messages et à la sauvegarde.</summary>
    public string CheminBase { get; } = cheminBase;

    /// <summary>
    /// Une connexion OUVERTE, clés étrangères actives. L'appelant la libère
    /// (<c>using</c>) ; le regroupement de connexions de Microsoft.Data.Sqlite
    /// rend l'ouverture négligeable.
    /// </summary>
    public SqliteConnection Ouvrir()
    {
        var connexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = CheminBase,
            // Crée le fichier s'il n'existe pas : une bibliothèque neuve est le
            // cas normal au premier lancement, pas une erreur.
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = regroupement,
        }.ConnectionString);

        connexion.Open();

        using (var pragma = connexion.CreateCommand())
        {
            // Les clés étrangères, donc. Voir le commentaire de classe.
            //
            // ----- WAL et délai d'attente : la correction d'un défaut RÉEL.
            //
            // En journal de restauration — le mode par défaut de SQLite — un
            // écrivain exclut les lecteurs, et une connexion qui trouve la base
            // occupée échoue IMMÉDIATEMENT sur « database is locked ». Tant que
            // l'application ne faisait qu'un aller-retour par clic, cela ne se
            // voyait pas. La complétion par lot, qui enchaîne lecture, écriture
            // et résolution de référentiels sur chaque ouvrage, l'a fait
            // apparaître dès le premier essai — et l'aurait fait apparaître chez
            // l'utilisateur, au milieu de trois cents fiches.
            //
            // WAL laisse un lecteur et un écrivain coexister ; il s'inscrit dans
            // le fichier de base, donc le poser à chaque ouverture ne coûte rien.
            // « busy_timeout » traite le reste : une base momentanément occupée
            // fait ATTENDRE cinq secondes au lieu de renvoyer une erreur — ce
            // qui, pour un défaut de quelques millisecondes, est la différence
            // entre rien du tout et un traitement interrompu.
            pragma.CommandText = """
                PRAGMA foreign_keys = ON;
                PRAGMA journal_mode = WAL;
                PRAGMA busy_timeout = 5000;
                """;
            pragma.ExecuteNonQuery();
        }

        return connexion;
    }

    /// <summary>
    /// Crée les tables si elles manquent. Le script est entièrement en
    /// <c>CREATE TABLE IF NOT EXISTS</c> : le rejouer à chaque démarrage ne
    /// coûte rien et évite d'avoir à distinguer « première fois » du reste.
    ///
    /// ⚠️ Ce mécanisme ne fait qu'AJOUTER. Le jour où une colonne devra
    /// changer de forme, il faudra un vrai script de migration — et SQLite ne
    /// sait pas modifier une colonne en place, il faut recréer la table. À
    /// prévoir avant la première vraie base remplie, pas après.
    /// </summary>
    public void CreerSiNecessaire(string schemaSql)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = schemaSql;
        commande.ExecuteNonQuery();
    }

    /// <summary>
    /// Rejoue les ajouts de colonnes sur une base existante — voir
    /// <see cref="Schema.Migrations"/> pour le pourquoi.
    ///
    /// L'échec est ignoré parce qu'il est le cas NORMAL : dès le deuxième
    /// démarrage, chaque instruction se heurte à une colonne déjà là. Aucun
    /// registre des migrations appliquées n'est donc tenu — la base elle-même
    /// fait foi, ce qui évite un journal capable de mentir sur son contenu.
    ///
    /// ⚠️ Le revers : une migration réellement fautive — faute de frappe, table
    /// absente — passerait tout aussi silencieusement. C'est tenable tant que
    /// les migrations restent de simples ajouts de colonne, dont un test vérifie
    /// le résultat. Ce ne le serait plus pour une transformation de données.
    /// </summary>
    public void AppliquerMigrations(IEnumerable<string> instructions)
    {
        using var connexion = Ouvrir();

        foreach (var instruction in instructions)
        {
            using var commande = connexion.CreateCommand();
            commande.CommandText = instruction;

            try
            {
                commande.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // Colonne déjà présente : attendu au démarrage suivant.
            }
        }
    }
}
