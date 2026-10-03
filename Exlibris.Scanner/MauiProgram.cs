using BarcodeScanning;
using Exlibris.Scanner.Services;
using Microsoft.Extensions.Logging;

namespace Exlibris.Scanner;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            // Sans cet appel, CameraView se compile, s'affiche noire et ne
            // détecte jamais rien. Le défaut ne se voit que sur l'appareil.
            .UseBarcodeScanning()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // En singleton : le relevé en cours est UN état, partagé par tout ce qui
        // le lit. Deux instances liraient le même fichier et se contrediraient.
        builder.Services.AddSingleton<InventaireEnCours>();
        builder.Services.AddSingleton<RechercheTitre>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
