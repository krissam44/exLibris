using Exlibris.Data;

namespace Exlibris.Tests;

/// <summary>
/// Banc d'essai : une base SQLite JETABLE, dans le dossier temporaire, et le
/// vrai schéma de production.
///
/// <b>Aucun test double.</b> Les dépôts d'Ex-libris sont essentiellement du
/// SQL — jointures, contraintes d'intégrité, tri des tomes — et c'est
/// précisément ce qui casse. Une suite bâtie sur des mocks ne vérifierait que
/// son propre câblage et laisserait passer exactement les défauts qu'on
/// cherche. C'est le choix qui a rendu possibles les 600 tests de Lirevia.
///
/// <b>Le schéma vient de l'assembly de production</b>
/// (<see cref="Schema.Sql"/>), jamais d'une copie : si la structure évolue sans
/// que le code suive, les tests le disent le jour même.
///
/// <b>S'INSTANCIE PAR TEST, jamais par classe.</b> Une classe de test le crée
/// dans un champ (<c>private readonly BancEssai _banc = new();</c>) et
/// l'libère dans <c>Dispose</c> : xUnit instanciant la classe une fois par
/// méthode, chaque test part d'une base vierge. Le passer en
/// <c>IClassFixture</c> ferait écrire tous les tests dans le même fichier —
/// ils passeraient isolément et échoueraient en suite.
///
/// <b>SQLite rend ce banc trivial</b> — là où Lirevia doit créer et détruire
/// une base MySQL, il suffit ici d'un fichier dans le dossier temporaire. Une
/// base par instance de test, donc aucune interférence entre classes, et rien
/// à nettoyer entre deux méthodes.
/// </summary>
public sealed class BancEssai : IDisposable
{
    private readonly string _dossier;

    public FabriqueConnexion Fabrique { get; }

    public BancEssai()
    {
        // Un dossier par instance, nommé au hasard : deux classes de test qui
        // tournent en parallèle ne doivent pas se marcher dessus. xUnit
        // parallélise par défaut entre classes, et un fichier partagé
        // produirait des échecs intermittents — les plus coûteux à
        // diagnostiquer.
        _dossier = Path.Combine(Path.GetTempPath(), "exlibris-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dossier);

        // ⚠️ SANS REGROUPEMENT DE CONNEXIONS. Voir FabriqueConnexion : c'est ce
        // qui permet à Dispose d'effacer le dossier sans vider un pool PARTAGÉ
        // par toutes les classes de test.
        Fabrique = new FabriqueConnexion(
            Path.Combine(_dossier, "exlibris.db"), regroupement: false);

        // Les RENOMMAGES d'abord : le schéma recréerait sinon une table vide
        // sous le nouveau nom, et le renommage suivant échouerait en
        // laissant les données dans l'ancienne. Voir MigrationsPrealables.
        Fabrique.AppliquerMigrations(Schema.MigrationsPrealables());
        Fabrique.CreerSiNecessaire(Schema.Sql());

        // Les migrations sont jouées ICI AUSSI, dans l'ordre du démarrage réel.
        // Une base de test créée sans elles serait structurellement différente
        // de celle de l'utilisateur, et la suite validerait une structure que
        // personne n'exécute jamais.
        Fabrique.AppliquerMigrations(Schema.Migrations());
    }

    public void Dispose()
    {
        // ⚠️ PAS DE ClearAllPools ICI. Il y en avait un — le regroupement garde
        // le fichier ouvert après la dernière libération, et la suppression
        // échouait sous Windows sans lui. Mais cette méthode est
        // PROCESSUS-GLOBALE : elle libérait les connexions des autres classes de
        // test, que xUnit exécute en parallèle, et celles-ci tombaient sur
        // « Cannot access a disposed object : SQLitePCL.sqlite3 ».
        //
        // L'échec ne se produisait qu'au premier passage après reconstruction —
        // le seul assez lent pour que deux classes se chevauchent vraiment —
        // donc invisible en développement et présent en intégration continue.
        // Le regroupement est désormais désactivé à la construction, ce qui
        // supprime la cause au lieu d'en traiter l'effet.
        try
        {
            Directory.Delete(_dossier, recursive: true);
        }
        catch (IOException)
        {
            // Un fichier encore verrouillé ne doit pas faire échouer une suite
            // par ailleurs verte. Le dossier temporaire se nettoie tout seul.
        }
    }
}
