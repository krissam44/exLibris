using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Exlibris.Dialogues;
using Exlibris.ViewModels;
using Exlibris.Vues;

namespace Exlibris.Services;

/// <summary>
/// Implémentation WPF de <see cref="IDialogueService"/> : des fenêtres
/// modales (<c>ShowDialog</c>), dont le résultat est rendu sous forme de tâche
/// déjà terminée.
///
/// Le service vit dans le projet WPF, et c'est normal : ouvrir une fenêtre est
/// une affaire d'interface. C'est l'INTERFACE que les ViewModels connaissent.
/// </summary>
public class DialogueService : IDialogueService
{
    public Task<bool> SaisirAsync(string titre, string aide, object formulaire, string libelleValider = "Enregistrer")
    {
        var dialogue = new DialogueBase
        {
            // Owner : sans lui, le dialogue s'ouvre au centre de l'ÉCRAN et
            // peut se retrouver derrière la fenêtre principale — un modal
            // invisible, et l'application qui paraît figée.
            Owner = Application.Current.MainWindow,
            Titre = titre,
            Aide = aide,
            Corps = VueDe(formulaire),
            LibelleValider = libelleValider,
        };

        return Task.FromResult(dialogue.ShowDialog() == true);
    }

    /// <summary>
    /// La vue WPF de chaque ViewModel de formulaire. Une vue NEUVE à chaque
    /// appel, sur le même ViewModel : un formulaire rouvert après un refus
    /// (titre manquant) retrouve ainsi la saisie intacte.
    ///
    /// Un ViewModel absent de cette liste est une erreur de programmation, et
    /// elle se voit au premier essai — d'où l'exception plutôt qu'un dialogue
    /// vide qui ferait croire à un formulaire sans champ.
    /// </summary>
    private static object VueDe(object formulaire) => formulaire switch
    {
        LivreFormulaireViewModel livre => new LivreFormulaireVue { DataContext = livre },
        SerieFormulaireViewModel serie => new SerieFormulaireVue { DataContext = serie },
        _ => throw new ArgumentException(
            $"Aucune vue WPF déclarée pour {formulaire.GetType().Name}.", nameof(formulaire)),
    };

    public Task<bool> ConfirmerAsync(string titre, string question, string libelleValider = "Confirmer")
    {
        var texte = new TextBlock
        {
            Text = question,
            TextWrapping = TextWrapping.Wrap,
        };

        var dialogue = new DialogueBase
        {
            Owner = Application.Current.MainWindow,
            Width = 480,
            Titre = titre,
            Corps = texte,
            LibelleValider = libelleValider,
            LibelleAnnuler = "Annuler",
        };

        return Task.FromResult(dialogue.ShowDialog() == true);
    }

    /// <summary>
    /// La couverture en grand.
    ///
    /// <b>La taille se déduit de l'ÉCRAN, pas de l'image.</b> Une couverture de
    /// 600 × 966 affichée à sa taille native déborderait d'un portable en
    /// 768 px de haut, et la fenêtre s'ouvrirait à cheval sur le bord. On part
    /// donc de la place disponible — 80 % de la zone de travail, hors barre des
    /// tâches — et on en déduit la largeur par les proportions de l'image.
    ///
    /// Le rapport vient de l'image RÉELLE et non d'une valeur supposée : les
    /// couvertures de bande dessinée à l'italienne ne suivent pas le format du
    /// poche, et une fenêtre calculée sur un rapport fixe leur laisserait de
    /// larges bandes vides.
    /// </summary>
    public Task AfficherImageAsync(string titre, string chemin)
    {
        var source = new Convertisseurs.ImageFichierConverter()
            .Convert(chemin, typeof(ImageSource), null, CultureInfo.CurrentCulture) as BitmapSource;

        if (source is null) return Task.CompletedTask;

        var hauteurMax = SystemParameters.WorkArea.Height * 0.8;
        var hauteurImage = Math.Min(source.PixelHeight, hauteurMax);
        var largeurImage = hauteurImage * source.PixelWidth / source.PixelHeight;

        var dialogue = new DialogueBase
        {
            Owner = Application.Current.MainWindow,
            Titre = titre,
            LectureSeule = true,
            LibelleAnnuler = "Fermer",

            // Les marges du gabarit (40 px au total) plus le cadre : sans elles,
            // l'image serait rognée de quelques pixels sur chaque bord.
            Width = largeurImage + 56,

            Corps = new Image
            {
                Source = source,
                Stretch = Stretch.Uniform,
                MaxHeight = hauteurMax,
            },
        };

        dialogue.ShowDialog();
        return Task.CompletedTask;
    }

    public Task<string?> ChoisirImageAsync()
    {
        var selecteur = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choisir une couverture",

            // « .jfif » est là parce que Windows l'emploie pour les JPEG
            // enregistrés depuis un navigateur — c'est-à-dire le cas le plus
            // fréquent de la couverture qu'on récupère à la main. Un filtre qui
            // l'oublie fait croire que le fichier n'existe pas.
            //
            // La dernière entrée montre TOUT : le filtre aide, il ne doit pas
            // enfermer. Le format réel est de toute façon reconnu aux octets, à
            // l'enregistrement.
            Filter = "Images|*.jpg;*.jpeg;*.jfif;*.jpe;*.png;*.gif;*.bmp;*.webp|Tous les fichiers|*.*",
            CheckFileExists = true,
        };

        return Task.FromResult(selecteur.ShowDialog() == true ? selecteur.FileName : null);
    }

    public Task<string?> ChoisirFichierAsync(string titre, string filtre)
    {
        var selecteur = new Microsoft.Win32.OpenFileDialog
        {
            Title = titre,
            Filter = filtre,
            CheckFileExists = true,
        };

        return Task.FromResult(selecteur.ShowDialog() == true ? selecteur.FileName : null);
    }
}
