using MudBlazor;

namespace Exlibris.Photino.MiseEnPage;

/// <summary>
/// Thème de l'application : la palette de Lirevia (MainLayout.razor.cs,
/// PaletteLight), la même que reprend Couleurs.xaml dans le WPF.
///
/// ⚠️ DUPLICATION assumée avec Lirevia, comme côté WPF : si la charte de
/// Lirevia change, ce fichier ne le saura pas. C'est le seul endroit à
/// corriger ici — aucune couleur n'est écrite ailleurs, hors app.css qui ne
/// reprend que les variables MudBlazor.
/// </summary>
public static class Theme
{
    public const string Ardoise = "#5478A5";  // barre du haut, pied de page, accents
    public const string Orange  = "#E8913A";  // libellés secondaires, valeurs à remarquer
    public const string Vert    = "#3A786D";  // confirmations
    public const string Rouge   = "#DB0016";  // même rouge que les boutons Fermer de Lirevia

    public static readonly MudTheme Exlibris = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = Ardoise,
            Secondary = Orange,
            Success = Vert,
            Error = Rouge,
            AppbarBackground = Ardoise,
            AppbarText = "#FFFFFF",
            Background = "#F5F5F0",
            Surface = "#FFFFFF",
            DrawerBackground = "#FAFAF8",
            DrawerText = "#1A1A1A",
            DrawerIcon = "#5C5C5C",
            TextPrimary = "#1A1A1A",
            TextSecondary = "#5C5C5C",
            LinesDefault = "#DEE2E6",
            Divider = "#DEE2E6",
        },
        // Boutons en casse normale, comme le WPF : « Choisir une image… » en
        // majuscules prenait deux lignes dans la colonne de la couverture.
        Typography = new Typography
        {
            Button = new ButtonTypography { TextTransform = "none", FontWeight = "500" },
        },
        LayoutProperties = new LayoutProperties
        {
            // 176 px ouvert, comme le menu du WPF ; réduit, la largeur d'une icône.
            DrawerWidthLeft = "176px",
            DrawerMiniWidthLeft = "52px",
            AppbarHeight = "44px",
            DefaultBorderRadius = "6px",
        },
    };
}
