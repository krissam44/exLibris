using Exlibris.Services;
using Exlibris.ViewModels;
using MudBlazor;
using Photino.NET;

namespace Exlibris.Photino.Services;

/// <summary>
/// Implémentation Photino de <see cref="IDialogueService"/> : dialogues
/// MudBlazor dans la fenêtre, et sélecteurs de fichiers NATIFS du système
/// (ceux de Windows, ou de GTK sous Linux) fournis par Photino.
///
/// Doit être enregistré en <c>scoped</c> : voir Program.cs.
/// </summary>
public sealed class DialogueService(IDialogService dialogues, PhotinoWindow fenetre) : IDialogueService
{
    public async Task<bool> SaisirAsync(string titre, string aide, object formulaire, string libelleValider = "Enregistrer")
    {
        // La correspondance ViewModel → vue Razor, pendant de VueDe() dans le
        // WPF. Les formulaires arrivent au fil des étapes du portage ; d'ici là,
        // on le DIT plutôt que d'ouvrir un dialogue vide.
        var etape = formulaire switch
        {
            LivreFormulaireViewModel => 3,
            _ => 4,
        };

        await dialogues.ShowMessageBoxAsync(titre,
            $"Ce formulaire n'est pas encore porté dans la version Photino (étape {etape}). "
            + "Il reste disponible dans la version WPF.",
            yesText: "Fermer");
        return false;
    }

    public async Task<bool> ConfirmerAsync(string titre, string question, string libelleValider = "Confirmer")
    {
        var reponse = await dialogues.ShowMessageBoxAsync(
            titre, question, yesText: libelleValider, cancelText: "Annuler",
            options: new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });

        // null = fermé par Échap ou par la croix : c'est un refus, comme dans le WPF.
        return reponse == true;
    }

    public Task AfficherImageAsync(string titre, string chemin) =>
        // Arrive avec le formulaire d'ouvrage, qui est le seul à l'employer.
        Task.CompletedTask;

    public Task<string?> ChoisirImageAsync() =>
        ChoisirFichierAsync("Choisir une couverture",
            // Même liste que le WPF, « .jfif » compris : c'est l'extension que
            // Windows donne aux JPEG enregistrés depuis un navigateur.
            "Images|*.jpg;*.jpeg;*.jfif;*.jpe;*.png;*.gif;*.bmp;*.webp|Tous les fichiers|*.*");

    public Task<string?> ChoisirFichierAsync(string titre, string filtre)
    {
        var choix = fenetre.ShowOpenFile(titre, defaultPath: null, multiSelect: false,
            filters: Filtres(filtre));

        return Task.FromResult(choix is { Length: > 0 } && !string.IsNullOrEmpty(choix[0]) ? choix[0] : null);
    }

    /// <summary>
    /// Traduit le filtre à la Windows (« Libellé|*.a;*.b|Autre|*.* ») dans la
    /// forme de Photino : un libellé et des extensions SANS « *. », qu'il
    /// préfixe lui-même pour Windows comme pour GTK.
    /// </summary>
    internal static (string Name, string[] Extensions)[] Filtres(string filtre)
    {
        var morceaux = filtre.Split('|');
        var filtres = new List<(string, string[])>();

        for (var i = 0; i + 1 < morceaux.Length; i += 2)
        {
            var extensions = morceaux[i + 1]
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.StartsWith("*.") ? e[2..] : e)
                .ToArray();
            filtres.Add((morceaux[i], extensions));
        }

        return [.. filtres];
    }
}
