using Microsoft.Extensions.DependencyInjection;

namespace Exlibris.Data;

/// <summary>
/// Composition des services de données, commune aux deux interfaces (WPF et
/// Photino).
///
/// <b>Pourquoi ici et non dans chaque application.</b> L'ordre de ces étapes
/// porte des pièges — les renommages avant le schéma, les migrations après,
/// un seul HttpClient — et deux copies finiraient par diverger : l'interface
/// qui aurait la mauvaise ouvrirait la base différemment de l'autre, sur les
/// mêmes fichiers.
/// </summary>
public static class Composition
{
    /// <summary>
    /// Dossier des données de l'utilisateur : <c>%LOCALAPPDATA%\Exlibris</c>
    /// sous Windows, <c>~/.local/share/Exlibris</c> sous Linux (même appel,
    /// .NET suit la convention XDG). Jamais à côté de l'exécutable : « Program
    /// Files » est en lecture seule, et une base posée près du binaire
    /// disparaîtrait à la prochaine réinstallation.
    /// </summary>
    public static string DossierDonnees() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Exlibris");

    /// <summary>
    /// Prépare la base (création et migrations) puis enregistre les dépôts et
    /// services de données.
    /// </summary>
    /// <param name="adresseLirevia">
    /// VIDE par défaut : Ex-libris est destiné à être distribué, et ceux qui
    /// l'installeront n'ont pas de serveur Lirevia. La source est alors
    /// simplement sautée.
    /// </param>
    public static IServiceCollection AddExlibrisDonnees(
        this IServiceCollection services, string dossierDonnees, string adresseLirevia)
    {
        Directory.CreateDirectory(dossierDonnees);

        var fabrique = new FabriqueConnexion(Path.Combine(dossierDonnees, "exlibris.db"));
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

        return services;
    }

    /// <summary>
    /// Mises en place à faire une fois le conteneur construit, à chaque
    /// démarrage. Les deux ne font rien dès le deuxième lancement.
    /// </summary>
    public static void InitialiserReferentiels(IServiceProvider services)
    {
        var referentiels = services.GetRequiredService<ReferentielDepot>();

        // La nomenclature de genres, posée une seule fois — la méthode ne fait
        // rien si la table contient déjà quelque chose. Voir
        // ReferentielDepot.SemerGenresSiVide pour pourquoi ce n'est pas
        // rejoué à chaque démarrage.
        referentiels.SemerGenresSiVide();

        // Rattrapage des clés d'éditeur, qui suivent désormais une règle plus
        // large. L'oublier ferait créer un doublon à chaque notice, la nouvelle
        // clé ne retrouvant pas l'ancienne. Voir RecalculerClesEditeurs.
        referentiels.RecalculerClesEditeurs();
    }
}
