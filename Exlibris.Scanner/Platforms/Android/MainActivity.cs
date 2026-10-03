using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Exlibris.Scanner;

// ScreenOrientation.Portrait (11/09/2026) : penché au-dessus d'une étagère basse,
// le téléphone basculait en paysage et la mire changeait de place au moment de
// viser. Android 16 ignore ce verrou sur les écrans de 600 dp et plus (tablettes,
// pliants) pour les applications qui le ciblent ; sur un téléphone, il tient.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ScreenOrientation = ScreenOrientation.Portrait, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
