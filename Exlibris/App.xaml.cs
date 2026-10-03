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

        // ----- La base et les services de données
        //
        // Composition partagée avec l'interface Photino : voir
        // Exlibris.Data.Composition pour l'ordre des migrations et le reste.
        services.AddExlibrisDonnees(
            Composition.DossierDonnees(),
            configuration["Lirevia:ApiUrl"] ?? string.Empty);

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

        Composition.InitialiserReferentiels(_services);

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
