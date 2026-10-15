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

        // Le dossier des données se déplace par la configuration
        // (Exlibris__DossierDonnees) : c'est ce qui permet d'essayer
        // l'application sur une COPIE de la base, sans jamais toucher la vraie.
        var dossierDonnees = configuration["Exlibris:DossierDonnees"] is { Length: > 0 } dossier
            ? dossier
            : Composition.DossierDonnees();

        // Les couvertures doivent être connues AVANT la construction : PhotinoX
        // reçoit sa source de fichiers du constructeur (voir Services.Couvertures).
        var couvertures = new Services.Couvertures(Path.Combine(dossierDonnees, "couvertures"));

        var builder = PhotinoBlazorApp.CreateBuilder(args)
            .UseFileProvider(_ => couvertures.SourceDeFichiers(Path.Combine(AppContext.BaseDirectory, "wwwroot")));
        var services = builder.Services;

        services.AddLogging();
        services.AddMudServices();
        services.AddSyncfusionBlazor();
        services.AddSingleton<ISyncfusionStringLocalizer, Services.SyncfusionFrancais>();

        services.AddExlibrisDonnees(dossierDonnees, configuration["Lirevia:ApiUrl"] ?? string.Empty);
        services.AddSingleton(couvertures);
        services.AddSingleton<Services.Fenetre>();

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
        services.AddScoped<SeriesViewModel>();
        services.AddScoped<InventaireViewModel>();
        services.AddScoped<ParametresViewModel>();

        builder.RootComponents.Add<App>("app");

        using var app = builder.Build();
        // La fenêtre, posée ici pour les sélecteurs de fichiers (voir Services.Fenetre).
        app.Services.GetRequiredService<Services.Fenetre>().Principale = app.MainWindow;
        Composition.InitialiserReferentiels(app.Services);

        // Le dossier servi doit être CELUI où CouvertureDepot range les images.
        // Les deux sont déduits du dossier des données par deux chemins ; s'ils
        // divergeaient un jour, toutes les couvertures disparaîtraient de
        // l'écran sans la moindre erreur. On préfère l'arrêt net au démarrage.
        var depot = app.Services.GetRequiredService<CouvertureDepot>();
        if (!string.Equals(Path.GetFullPath(depot.Dossier), couvertures.Dossier, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Dossier des couvertures incohérent : {depot.Dossier} ≠ {couvertures.Dossier}");

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
            app.MainWindow.ShowMessage("Erreur", e.ExceptionObject.ToString() ?? "Erreur inconnue", icon: global::Photino.NET.PhotinoDialogIcon.Error);

        app.Run();
    }
}
