using System.Windows;
using Syncfusion.Windows.Shared;

namespace Exlibris.Dialogues;

/// <summary>
/// Gabarit commun des dialogues : bandeau ardoise, contenu, pied à deux
/// boutons. Les fenêtres qui en héritent n'ont qu'à fournir leur contenu et
/// leurs libellés.
///
/// <b>Le résultat passe par <see cref="Window.DialogResult"/></b>, pas par un
/// événement : c'est ce que <c>ShowDialog</c> rend, et l'appelant reste une
/// seule ligne. Un événement obligerait chaque appelant à s'abonner puis à se
/// désabonner — trois lignes de plus, et une fuite le jour où l'on oublie la
/// seconde.
/// </summary>
public partial class DialogueBase : ChromelessWindow
{
    public DialogueBase()
    {
        InitializeComponent();
    }

    /// <summary>Titre affiché DANS le dialogue, sous le bandeau.</summary>
    public string Titre
    {
        get => TitreInterne.Text;
        set => TitreInterne.Text = value;
    }

    /// <summary>
    /// Phrase d'explication sous le titre. Masquée si vide : une ligne blanche
    /// sous un titre se remarque plus qu'on ne croit.
    /// </summary>
    public string Aide
    {
        get => AideInterne.Text;
        set
        {
            AideInterne.Text = value;
            AideInterne.Visibility = string.IsNullOrWhiteSpace(value)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    /// <summary>Le corps du dialogue, fourni par la classe dérivée.</summary>
    public object? Corps
    {
        get => ZoneContenu.Content;
        set => ZoneContenu.Content = value;
    }

    public string LibelleValider
    {
        get => (string)BoutonValider.Content;
        set => BoutonValider.Content = value;
    }

    public string LibelleAnnuler
    {
        get => (string)BoutonAnnuler.Content;
        set => BoutonAnnuler.Content = value;
    }

    /// <summary>
    /// Masque le bouton d'action, pour un dialogue de simple lecture. Le bouton
    /// « Fermer » devient alors le seul, et prend la place par défaut.
    /// </summary>
    public bool LectureSeule
    {
        set
        {
            BoutonValider.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            BoutonAnnuler.IsDefault = value;
        }
    }

    /// <summary>
    /// Appelée avant de fermer sur validation. Une classe dérivée la redéfinit
    /// pour contrôler sa saisie : renvoyer <c>false</c> laisse le dialogue
    /// ouvert, l'utilisateur devant sa correction.
    /// </summary>
    protected virtual bool Valider() => true;

    private void Valider_Click(object sender, RoutedEventArgs e)
    {
        if (!Valider()) return;
        DialogResult = true;
    }

    private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
