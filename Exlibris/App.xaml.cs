using Exlibris.Data;
using Exlibris.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Syncfusion.Licensing;
using System.Configuration;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace Exlibris;

/// <summary>
/// Démarrage de l'application : composition des dépendances, puis ouverture de
/// la fenêtre.
///
/// L'injection est posée dès le squelette, alors qu'un seul ViewModel en
/// dépend. La raison est le chemin de la base : la fabrique de connexions doit
/// être construite UNE fois, avec le chemin réel, et distribuée telle quelle.
/// La rattraper après coup obligerait à retoucher chaque ViewModel déjà écrit.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Lue UNE fois, pour la licence Syncfusion comme pour l'adresse de
        // Lirevia. Deux lectures séparées finiraient par diverger sur les
        // fichiers consultés ou leur ordre.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();

        // ----- La base
        //
        // Dans les données d'application de l'utilisateur, jamais à côté de
        // l'exécutable : « Program Files » est en lecture seule, et une base
        // posée près du binaire disparaîtrait à la prochaine réinstallation.
        var dossier = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Exlibris");
        Directory.CreateDirectory(dossier);

        var fabrique = new FabriqueConnexion(Path.Combine(dossier, "exlibris.db"));
        // Les RENOMMAGES d'abord : le schéma recréerait sinon une table vide
        // sous le nouveau nom, et le renommage suivant échouerait en
        // laissant les données dans l'ancienne. Voir MigrationsPrealables.
        fabrique.AppliquerMigrations(Schema.MigrationsPrealables());
        fabrique.CreerSiNecessaire(Schema.Sql());

        // Les colonnes ajoutées après coup. Indispensable ici et pas seulement
        // sur une base neuve : le schéma est en « CREATE TABLE IF NOT EXISTS »,
        // qui ne fait RIEN sur une table déjà présente. Sans cette ligne, une
        // évolution marcherait sur une base vierge et échouerait sur la vôtre.
        fabrique.AppliquerMigrations(Schema.Migrations());
        services.AddSingleton(fabrique);
        services.AddSingleton<LivreDepot>();
        services.AddSingleton<ReferentielDepot>();

        // Un SEUL HttpClient pour toute la vie de l'application. En créer un par
        // recherche épuiserait les sockets — le défaut classique de HttpClient,
        // qui ne se voit qu'après quelques centaines d'appels, donc exactement
        // au moment où l'on enrichit un fonds par lots.
        services.AddSingleton(new HttpClient
        {
            // Court volontairement : la saisie attend. Passé ce délai, taper le
            // titre à la main est plus rapide que d'attendre la réponse.
            Timeout = TimeSpan.FromSeconds(8),
        });
        // Construit à la main, et non résolu par type : le conteneur ne sait pas
        // fournir une `string`, et un paramètre optionnel ne le sauve pas — il
        // échouerait à la résolution, au premier clic sur « Rechercher ».
        //
        // Adresse VIDE par défaut : Ex-libris est destiné à être distribué, et
        // ceux qui l'installeront n'ont pas de serveur Lirevia. La source est
        // alors simplement sautée.
        var adresseLirevia = configuration["Lirevia:ApiUrl"] ?? string.Empty;
        services.AddSingleton(fournisseur => new NoticeEnLigneService(
            fournisseur.GetRequiredService<HttpClient>(), adresseLirevia));
        services.AddSingleton<CouvertureDepot>();
        services.AddSingleton<ParametreDepot>();
        services.AddSingleton<InventaireDepot>();

        // CompletionParLot reçoit de quoi chercher et de quoi rapporter une
        // image, plutôt que les services eux-mêmes : c'est ce qui permet à la
        // suite de tests de lui fournir des notices toutes prêtes, sans
        // réseau. Une fabrique et non un singleton — chaque traitement part
        // avec son propre jeton d'annulation.
        services.AddSingleton<Func<CompletionParLot>>(f => () => new CompletionParLot(
            f.GetRequiredService<InventaireDepot>(),
            f.GetRequiredService<LivreDepot>(),
            f.GetRequiredService<ReferentielDepot>(),
            chercher: (isbn, token) =>
                f.GetRequiredService<NoticeEnLigneService>().ChercherAsync(isbn, token),
            rapporterCouverture: (url, token) =>
                f.GetRequiredService<CouvertureDepot>().TelechargerAsync(url, token)));

        // L'ouverture de fenêtres passe par une interface : un ViewModel qui
        // construirait lui-même une Window deviendrait impossible à éprouver
        // hors interface.
        services.AddSingleton<Services.IDialogueService, Services.DialogueService>();

        // ----- Syncfusion
        EnregistrerLicenceSyncfusion(configuration);

        // ----- Les écrans
        //
        // En singleton, et c'est délibéré : une rubrique conserve son état
        // d'une visite à l'autre. Revenir sur la Bibliothèque doit retrouver la
        // recherche en cours, pas recharger 7 000 livres.
        services.AddSingleton<AccueilViewModel>();
        services.AddSingleton<BibliothequeViewModel>();
        services.AddSingleton<SeriesViewModel>();
        services.AddSingleton<InventaireViewModel>();
        services.AddSingleton<ParametresViewModel>();
        services.AddSingleton<MainViewModel>();

        _services = services.BuildServiceProvider();

        // La nomenclature de genres, posée une seule fois — la méthode ne fait
        // rien si la table contient déjà quelque chose. Voir
        // ReferentielDepot.SemerGenresSiVide pour pourquoi ce n'est pas
        // rejoué à chaque démarrage.
        var referentiels = _services.GetRequiredService<ReferentielDepot>();
        referentiels.SemerGenresSiVide();

        // Rattrapage des clés d'éditeur, qui suivent désormais une règle plus
        // large. Ne fait rien dès le deuxième lancement — mais l'oublier ferait
        // créer un doublon à chaque notice, la nouvelle clé ne retrouvant pas
        // l'ancienne. Voir RecalculerClesEditeurs.
        referentiels.RecalculerClesEditeurs();

        new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() }.Show();
    }

    /// <summary>
    /// Enregistre la licence Syncfusion lue dans la configuration — même
    /// dispositif que MailZen, pour que les deux applications s'administrent de
    /// la même façon.
    ///
    /// <b>Ce que cela protège, et ce que cela ne protège pas.</b> La clé sort du
    /// code source : elle ne part donc plus dans une archive, un dépôt ou un
    /// envoi de fichiers. Elle reste en revanche présente, en clair, à côté de
    /// l'exécutable sur les trois postes — c'est inévitable pour une licence
    /// applicative, que Syncfusion doit lire au démarrage sans réseau. Le but
    /// est de ne pas la publier, pas de la rendre illisible.
    ///
    /// Trois sources, la dernière l'emportant : le fichier versionné (structure
    /// et valeurs vides), le fichier local (la vraie clé, non versionné), puis
    /// les variables d'environnement — <c>Syncfusion__LicenseKey</c>, pratique
    /// pour un poste où l'on ne veut poser aucun fichier.
    ///
    /// Sans clé, Syncfusion affiche sa bannière d'évaluation mais l'application
    /// démarre : on ne bloque pas le lancement pour cela.
    /// </summary>
    private static void EnregistrerLicenceSyncfusion(IConfiguration configuration)
    {
        var cle = configuration["Syncfusion:LicenseKey"];
        if (!string.IsNullOrWhiteSpace(cle))
            SyncfusionLicenseProvider.RegisterLicense(cle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
