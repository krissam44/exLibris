using Exlibris.Shared.Modeles;

namespace Exlibris.Data;

/// <summary>
/// Complète en série les fiches qu'un inventaire a laissées sans titre.
///
/// <b>La recherche et le téléchargement sont REÇUS, pas construits ici</b>, et
/// c'est ce qui rend cette classe éprouvable. Un service qui irait lui-même sur
/// le réseau ne pourrait se tester qu'avec une connexion, des quotas intacts et
/// de la patience — autant dire jamais. Ici un test fournit des notices toutes
/// prêtes et vérifie ce qui compte : ce qui est écrit, ce qui est préservé, et
/// que la file avance même quand rien n'est trouvé.
/// </summary>
public class CompletionParLot(
    InventaireDepot inventaire,
    LivreDepot livres,
    ReferentielDepot referentiels,
    Func<string, CancellationToken, Task<NoticeEnLigne?>> chercher,
    Func<string, CancellationToken, Task<string>> rapporterCouverture)
{
    /// <summary>Ce qu'un passage a produit.</summary>
    public record Bilan(int Traitees, int Completees, int Introuvables)
    {
        public int Restantes { get; init; }
    }

    /// <summary>
    /// Le temps d'attente entre deux ouvrages.
    ///
    /// <b>Ce n'est pas une précaution de façade.</b> La BnF, Google et Open
    /// Library sont gratuits et publics ; les marteler à trois cents requêtes en
    /// vingt secondes ferait bannir l'adresse — et avec elle la recherche à la
    /// saisie, celle qui rend service tous les jours. Une demi-seconde met une
    /// heure pour sept mille albums, ce qui est le rythme d'un traitement qu'on
    /// lance et qu'on laisse tourner.
    /// </summary>
    public static readonly TimeSpan PauseParDefaut = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Réglable pour que la suite de tests n'attende pas. Une pause de politesse
    /// qui ralentit les tests finit par être supprimée « juste pour voir », et
    /// c'est le service réel qui se met alors à marteler les sources.
    /// </summary>
    public TimeSpan Pause { get; init; } = PauseParDefaut;

    /// <summary>
    /// Traite un lot. Rend la main dès que <paramref name="token"/> est annulé,
    /// en ayant marqué tout ce qui a été tenté — reprendre plus tard repart donc
    /// exactement d'où l'on s'était arrêté.
    /// </summary>
    /// <param name="progression">
    /// Appelée après chaque ouvrage, avec la ligne à afficher. C'est le seul
    /// retour que l'utilisateur aura pendant plusieurs minutes : une barre qui
    /// avance sans rien dire laisse croire à un blocage.
    /// </param>
    public async Task<Bilan> TraiterAsync(
        int taille = 50,
        Action<string>? progression = null,
        CancellationToken token = default)
    {
        var traitees = 0;
        var completees = 0;
        var introuvables = 0;

        foreach (var attente in inventaire.ACompleter(taille))
        {
            if (token.IsCancellationRequested) break;

            traitees++;

            NoticeEnLigne? notice = null;
            try
            {
                notice = await chercher(attente.Isbn, token);
            }
            catch (OperationCanceledException)
            {
                // Annulation pendant l'appel : la fiche n'a pas eu sa chance, on
                // ne la marque pas. Elle repassera au prochain lot.
                traitees--;
                break;
            }
            catch (Exception)
            {
                // Une source qui déraille ne doit pas arrêter trois cents
                // ouvrages. La fiche est marquée tentée comme les autres.
            }

            if (notice is null || !notice.EstUtilisable)
            {
                introuvables++;
                inventaire.MarquerTentee(attente.Id);
                progression?.Invoke($"✕ {attente.Isbn} — introuvable");
                await AttendreAsync(token);
                continue;
            }

            var livre = livres.ParId(attente.Id);
            if (livre is null) continue;   // supprimée entre-temps

            AppliquerAsync(livre, notice);

            if (livre.Couverture.Length == 0 && notice.CouvertureUrl.Length > 0)
                livre.Couverture = await rapporterCouverture(notice.CouvertureUrl, token);

            livres.MettreAJour(livre);
            EnregistrerAuteurs(livre.Id, notice.Auteurs);

            inventaire.MarquerTentee(attente.Id);
            completees++;
            progression?.Invoke($"✓ {livre.Titre} — {notice.Source}");

            await AttendreAsync(token);
        }

        return new Bilan(traitees, completees, introuvables)
        {
            Restantes = inventaire.NombreACompleter(),
        };
    }

    /// <summary>
    /// Reporte la notice sur la fiche, <b>sans jamais écraser un champ déjà
    /// rempli</b> — même règle que la recherche à la saisie, et pour la même
    /// raison : un traitement par lot qui passerait sur trois cents fiches
    /// effacerait vos corrections plus vite que vous ne les avez faites.
    /// </summary>
    private void AppliquerAsync(Livre livre, NoticeEnLigne notice)
    {
        livre.Titre = NoticeEnLigne.Completer(livre.Titre, notice.Titre).Valeur;

        if (livre.EditeurId is null && notice.Editeur.Length > 0)
            livre.EditeurId = referentiels.ResoudreEditeur(notice.Editeur);

        if (livre.GenreId is null && notice.Genre.Length > 0)
            livre.GenreId = referentiels.ResoudreGenre(notice.Genre);

        livre.Annee ??= notice.Annee;

        // La provenance, pour la même raison que dans Lirevia : savoir d'où
        // vient une fiche dit ce qu'on peut en croire.
        if (livre.SourceNotice.Length == 0) livre.SourceNotice = notice.Source;
    }

    /// <summary>
    /// Les auteurs, seulement si la fiche n'en avait aucun.
    ///
    /// <c>DefinirAuteurs</c> fait table rase avant de réécrire : appelé sur une
    /// fiche déjà renseignée, il remplacerait des rôles corrigés à la main par
    /// des « auteur » indéterminés.
    /// </summary>
    private void EnregistrerAuteurs(long livreId, string auteurs)
    {
        if (auteurs.Length == 0) return;

        var existants = livres.ParId(livreId)?.Auteurs ?? string.Empty;
        if (existants.Length > 0) return;

        var resolus = auteurs
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(nom => referentiels.ResoudreAuteur(nom))
            .Where(id => id is not null)
            .Select(id => (id!.Value, RoleAuteur.Indetermine));

        livres.DefinirAuteurs(livreId, resolus);
    }

    private async Task AttendreAsync(CancellationToken token)
    {
        if (Pause <= TimeSpan.Zero) return;

        try { await Task.Delay(Pause, token); }
        catch (OperationCanceledException) { /* l'appelant sort de la boucle */ }
    }
}
