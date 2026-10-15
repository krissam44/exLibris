using Exlibris.Services;
using Exlibris.ViewModels;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Exlibris.Photino.Services;

/// <summary>
/// Implémentation Photino de <see cref="IDialogueService"/> : dialogues
/// MudBlazor dans la fenêtre, et sélecteurs de fichiers NATIFS du système
/// (ceux de Windows, ou de GTK sous Linux) fournis par Photino.
///
/// Doit être enregistré en <c>scoped</c> : voir Program.cs.
/// </summary>
public sealed class DialogueService(IDialogService dialogues, Fenetre fenetres, Couvertures couvertures)
    : IDialogueService
{
    /// <summary>
    /// Les options communes : on ne ferme PAS un formulaire en cliquant à côté.
    /// Une saisie de dix champs perdue sur un clic égaré est exactement ce
    /// qu'un dialogue modal du WPF ne permettait pas. Échap, en revanche,
    /// ferme — c'était le bouton IsCancel du WPF.
    /// </summary>
    private static DialogOptions Options(MaxWidth largeur) => new()
    {
        MaxWidth = largeur,
        FullWidth = true,
        CloseOnEscapeKey = true,
        BackdropClick = false,
        NoHeader = false,
        CloseButton = false,
    };

    public async Task<bool> SaisirAsync(string titre, string aide, object formulaire, string libelleValider = "Enregistrer")
    {
        // La correspondance ViewModel → vue Razor, pendant de VueDe() dans le
        // WPF. Un formulaire pas encore porté le DIT plutôt que d'ouvrir un
        // dialogue vide.
        // Un ViewModel absent de cette liste est une erreur de programmation,
        // et elle se voit au premier essai — comme VueDe() côté WPF.
        var (vue, largeur) = formulaire switch
        {
            // 820 px dans le WPF : Medium (960) au plus, le formulaire et sa
            // couverture y tiennent sans défilement horizontal.
            LivreFormulaireViewModel => (typeof(Formulaires.LivreFormulaire), MaxWidth.Medium),
            // Trois champs sur 400 px dans le WPF.
            SerieFormulaireViewModel => (typeof(Formulaires.SerieFormulaire), MaxWidth.Small),
            _ => throw new ArgumentException(
                $"Aucune vue Razor déclarée pour {formulaire.GetType().Name}.", nameof(formulaire)),
        };

        var parametres = new DialogParameters<Dialogues.FormulaireDialogue>
        {
            { d => d.Titre, titre },
            { d => d.Aide, aide },
            { d => d.LibelleValider, libelleValider },
            { d => d.TypeVue, vue },
            { d => d.Formulaire, formulaire },
        };

        var dialogue = await dialogues.ShowAsync<Dialogues.FormulaireDialogue>(null, parametres, Options(largeur));
        var resultat = await dialogue.Result;
        return resultat is { Canceled: false };
    }

    /// <summary>
    /// La fiche d'un ouvrage en consultation (double-clic dans la
    /// bibliothèque) : la vue LivreConsultation, sans bouton d'enregistrement.
    /// Propre à la version Photino, d'où son absence de IDialogueService.
    /// </summary>
    public async Task ConsulterAsync(string titre, LivreFormulaireViewModel formulaire)
    {
        var parametres = new DialogParameters<Dialogues.FormulaireDialogue>
        {
            { d => d.Titre, titre },
            { d => d.LectureSeule, true },
            { d => d.TypeVue, typeof(Formulaires.LivreConsultation) },
            { d => d.Formulaire, formulaire },
        };
        await (await dialogues.ShowAsync<Dialogues.FormulaireDialogue>(null, parametres, Options(MaxWidth.Medium))).Result;
    }

    public async Task<bool> ConfirmerAsync(string titre, string question, string libelleValider = "Confirmer")
    {
        // Les retours à la ligne de la question sont CONSERVÉS : la confirmation
        // de fusion nomme le survivant, puis la liste des absorbés, puis la
        // conséquence — trois paragraphes, comme dans le WPF. En texte brut, le
        // HTML les fondait en un seul bloc où l'on ne voyait plus qui survit.
        // Le texte est encodé AVANT d'y insérer les <br> : un libellé contenant
        // un chevron reste un libellé.
        var corps = new MarkupString(
            System.Net.WebUtility.HtmlEncode(question).Replace("\n", "<br />"));

        var reponse = await dialogues.ShowMessageBoxAsync(
            titre, corps, yesText: libelleValider, cancelText: "Annuler",
            options: Options(MaxWidth.Small));

        // null = fermé par Échap ou par la croix : c'est un refus, comme dans le WPF.
        return reponse == true;
    }

    public async Task AfficherImageAsync(string titre, string chemin)
    {
        if (couvertures.Url(chemin) is not { } url) return;

        var parametres = new DialogParameters<Dialogues.ImageDialogue>
        {
            { d => d.Titre, titre },
            { d => d.Url, url },
        };

        // Largeur libre : c'est l'image, dimensionnée sur la HAUTEUR de l'écran,
        // qui décide (voir ImageDialogue).
        // Rien à perdre ici : un clic à côté referme.
        var options = Options(MaxWidth.False) with { FullWidth = false, BackdropClick = true };

        await (await dialogues.ShowAsync<Dialogues.ImageDialogue>(null, parametres, options)).Result;
    }

    public Task<string?> ChoisirImageAsync() =>
        ChoisirFichierAsync("Choisir une couverture",
            // Même liste que le WPF, « .jfif » compris : c'est l'extension que
            // Windows donne aux JPEG enregistrés depuis un navigateur.
            "Images|*.jpg;*.jpeg;*.jfif;*.jpe;*.png;*.gif;*.bmp;*.webp|Tous les fichiers|*.*");

    /// <summary>
    /// La version ASYNCHRONE du sélecteur, jamais la synchrone : la
    /// documentation de PhotinoX le dit (« Async version is required for
    /// PhotinoX.Blazor as synchronous version crashes »), et Budgevia l'a
    /// vécu — après « Annuler », plus un bouton ne répondait.
    /// </summary>
    public async Task<string?> ChoisirFichierAsync(string titre, string filtre)
    {
        var choix = await fenetres.Principale.ShowOpenFileAsync(titre, defaultPath: null, multiSelect: false,
            filters: Filtres(filtre));

        return choix is { Length: > 0 } && !string.IsNullOrEmpty(choix[0]) ? choix[0] : null;
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
