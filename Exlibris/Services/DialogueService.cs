using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Exlibris.Dialogues;

namespace Exlibris.Services;

/// <summary>
/// Ouvre les dialogues pour le compte des ViewModels.
///
/// <b>Pourquoi un service plutôt qu'un appel direct.</b> Un ViewModel qui
/// construirait lui-même une <c>Window</c> deviendrait impossible à éprouver
/// hors interface : le premier test qui l'exercerait ouvrirait une vraie
/// fenêtre et attendrait un clic. En passant par une interface, un test
/// substitue une implémentation qui répond « validé » sans rien afficher.
///
/// Le service vit dans le projet WPF, et c'est normal : ouvrir une fenêtre est
/// une affaire d'interface. C'est l'INTERFACE que les ViewModels connaissent.
/// </summary>
public interface IDialogueService
{
    /// <summary>
    /// Affiche <paramref name="corps"/> dans le gabarit commun et rend
    /// <c>true</c> si l'utilisateur a validé.
    /// </summary>
    bool Saisir(string titre, string aide, object corps, string libelleValider = "Enregistrer");

    /// <summary>Question fermée. <c>true</c> si l'utilisateur confirme.</summary>
    bool Confirmer(string titre, string question, string libelleValider = "Confirmer");

    /// <summary>Montre une image en grand, sans autre action que fermer.</summary>
    void AfficherImage(string titre, string chemin);

    /// <summary>
    /// Demande un fichier image. Renvoie son chemin, ou <c>null</c> si
    /// l'utilisateur renonce.
    /// </summary>
    string? ChoisirImage();

    /// <summary>
    /// Demande un fichier quelconque. <paramref name="filtre"/> suit la
    /// convention Windows : « Libellé|*.ext;*.ext2|Autre libellé|*.* ».
    /// </summary>
    string? ChoisirFichier(string titre, string filtre);
}

public class DialogueService : IDialogueService
{
    public bool Saisir(string titre, string aide, object corps, string libelleValider = "Enregistrer")
    {
        var dialogue = new DialogueBase
        {
            // Owner : sans lui, le dialogue s'ouvre au centre de l'ÉCRAN et
            // peut se retrouver derrière la fenêtre principale — un modal
            // invisible, et l'application qui paraît figée.
            Owner = Application.Current.MainWindow,
            Titre = titre,
            Aide = aide,
            Corps = corps,
            LibelleValider = libelleValider,
        };

        return dialogue.ShowDialog() == true;
    }

    public bool Confirmer(string titre, string question, string libelleValider = "Confirmer")
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

        return dialogue.ShowDialog() == true;
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
    public void AfficherImage(string titre, string chemin)
    {
        var source = new Convertisseurs.ImageFichierConverter()
            .Convert(chemin, typeof(ImageSource), null, CultureInfo.CurrentCulture) as BitmapSource;

        if (source is null) return;

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
    }

    public string? ChoisirImage()
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

        return selecteur.ShowDialog() == true ? selecteur.FileName : null;
    }

    public string? ChoisirFichier(string titre, string filtre)
    {
        var selecteur = new Microsoft.Win32.OpenFileDialog
        {
            Title = titre,
            Filter = filtre,
            CheckFileExists = true,
        };

        return selecteur.ShowDialog() == true ? selecteur.FileName : null;
    }
}
