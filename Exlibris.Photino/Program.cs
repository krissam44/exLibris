using System.Globalization;
using Exlibris.Data;
using Exlibris.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Photino.Blazor;

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

        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        var services = builder.Services;

        services.AddLogging();
        services.AddMudServices();

        services.AddExlibrisDonnees(
            Composition.DossierDonnees(),
            configuration["Lirevia:ApiUrl"] ?? string.Empty);

        // ----- Les écrans
        //
        // En singleton, comme dans le WPF : une rubrique conserve son état
        // d'une visite à l'autre. Revenir sur la Bibliothèque doit retrouver la
        // recherche en cours, pas recharger 7 000 livres.
        services.AddSingleton<AccueilViewModel>();

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
