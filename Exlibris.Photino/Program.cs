using System.Globalization;
using Exlibris.Data;
using Exlibris.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Photino.Blazor;
using Syncfusion.Blazor;

namespace Exlibris.Photino;

/// <summary>
/// Démarrage : configuration, services de données (partagés avec le WPF),
/// écrans, puis ouverture de la fenêtre.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Culture imposée : sous Linux elle vient de LANG, souvent C.UTF-8 →
        // culture invariante, « 12.5 » et dates à l'américaine. Le rendu est
        // ainsi le même sur tous les systèmes.
        var fr = new CultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = fr;
        CultureInfo.DefaultThreadCurrentUICulture = fr;

        // Mêmes sources et même ordre que le WPF : fichier versionné (valeurs
        // vides), fichier local (vraies clés), variables d'environnement.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Même dispositif que le WPF (voir App.EnregistrerLicenceSyncfusion) :
        // sans clé, bannière d'évaluation, mais l'application démarre.
        if (configuration["Syncfusion:LicenseKey"] is { Length: > 0 } cle)
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(cle);

        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        var services = builder.Services;

        services.AddLogging();
        services.AddMudServices();
        services.AddSyncfusionBlazor();
        services.AddSingleton<ISyncfusionStringLocalizer, Services.SyncfusionFrancais>();

        // Le dossier des données se déplace par la configuration
        // (Exlibris__DossierDonnees) : c'est ce qui permet d'essayer
        // l'application sur une COPIE de la base, sans jamais toucher la vraie.
        services.AddExlibrisDonnees(
            configuration["Exlibris:DossierDonnees"] is { Length: > 0 } dossier
                ? dossier
                : Composition.DossierDonnees(),
            configuration["Lirevia:ApiUrl"] ?? string.Empty);

        // ----- Les écrans et les dialogues
        //
        // En SCOPED, et non en singleton comme dans le WPF — mais pour le même
        // effet. Photino n'ouvre qu'une fenêtre, donc qu'une portée pour toute
        // la vie de l'application : une rubrique conserve bien son état d'une
        // visite à l'autre. La raison du scoped est le service de dialogues de
        // MudBlazor, scoped lui-même : résolu depuis la racine, un singleton en
        // recevrait une AUTRE instance que celle du MudDialogProvider affiché,
        // et aucun dialogue n'apparaîtrait jamais.
        services.AddScoped<Exlibris.Services.IDialogueService, Services.DialogueService>();
        services.AddScoped<AccueilViewModel>();
        services.AddScoped<BibliothequeViewModel>();

        builder.RootComponents.Add<App>("app");

        var app = builder.Build();
        Composition.InitialiserReferentiels(app.Services);

        app.MainWindow
            // Photino trace par défaut chaque échange avec la WebView : des
            // pages de base64 dans le terminal. 0 = muet, 2 pour diagnostiquer.
            .SetLogVerbosity(0)
            // Titre court : sous Linux, Photino.Native le tronque vers 30 caractères.
            .SetTitle("Ex-libris")
            // .ico sous Windows (plusieurs tailles, net dans la barre des
            // tâches) ; GTK ne lit pas ce format, d'où le PNG ailleurs.
            .SetIconFile(Path.Combine(AppContext.BaseDirectory, "Assets",
                OperatingSystem.IsWindows() ? "exlibris.ico" : "exlibris-256.png"))
            // Taille de RESTAURATION, celle que reprend la fenêtre quand on la
            // sort du plein écran — même rôle que Height/Width dans le WPF.
            .SetSize(1280, 800)
            .Center()
            .SetMaximized(true);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            app.MainWindow.ShowMessage("Erreur", e.ExceptionObject.ToString());

        app.Run();
    }
}
