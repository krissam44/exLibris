namespace Exlibris.Scanner;

/// <summary>
/// Une seule page, donc pas de Shell.
///
/// Le modèle de projet en pose un, qui sert à la navigation et aux menus
/// latéraux. Ici il n'y a rien vers quoi naviguer : le garder aurait ajouté une
/// barre de titre, un cycle de vie de plus, et surtout l'impossibilité
/// d'injecter la page — un ShellContent instancie sa page par réflexion, sans
/// passer par le conteneur.
/// </summary>
public partial class App(IServiceProvider services) : Application
{
    public App() : this(null!) => throw new InvalidOperationException(
        "App doit être construite par l'injection : voir MauiProgram.");

    protected override Window CreateWindow(IActivationState? etat) =>
        new(services.GetRequiredService<MainPage>());
}
