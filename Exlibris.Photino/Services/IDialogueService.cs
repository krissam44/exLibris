namespace Exlibris.Services;

/// <summary>
/// Ouvre les dialogues pour le compte des ViewModels.
///
/// <b>Pourquoi un service plutôt qu'un appel direct.</b> Un ViewModel qui
/// construirait lui-même une fenêtre deviendrait impossible à éprouver hors
/// interface : le premier test qui l'exercerait ouvrirait une vraie fenêtre et
/// attendrait un clic. En passant par une interface, un test substitue une
/// implémentation qui répond « validé » sans rien afficher.
///
/// <b>Asynchrone, et c'est imposé par Blazor.</b> Un dialogue WPF bloque
/// l'appelant jusqu'à la réponse (<c>ShowDialog</c>) ; un dialogue Blazor rend
/// la main tout de suite et répond plus tard. Une seule interface pour les deux
/// interfaces graphiques doit donc être asynchrone — l'implémentation WPF rend
/// simplement une tâche déjà terminée.
///
/// <b>Les formulaires sont désignés par leur ViewModel, jamais par une vue.</b>
/// Chaque interface sait quelle vue montrer pour quel ViewModel ; le ViewModel,
/// lui, n'a pas à savoir s'il sera dessiné en XAML ou en Razor. C'est ce qui
/// permet aux deux interfaces de partager les mêmes ViewModels.
///
/// Ce fichier ne dépend d'aucun type WPF : le projet Photino le reprend tel quel.
/// </summary>
public interface IDialogueService
{
    /// <summary>
    /// Affiche le formulaire porté par <paramref name="formulaire"/> (un
    /// ViewModel connu de l'interface) dans le gabarit commun, et rend
    /// <c>true</c> si l'utilisateur a validé.
    /// </summary>
    Task<bool> SaisirAsync(string titre, string aide, object formulaire, string libelleValider = "Enregistrer");

    /// <summary>Question fermée. <c>true</c> si l'utilisateur confirme.</summary>
    Task<bool> ConfirmerAsync(string titre, string question, string libelleValider = "Confirmer");

    /// <summary>Montre une image en grand, sans autre action que fermer.</summary>
    Task AfficherImageAsync(string titre, string chemin);

    /// <summary>
    /// Demande un fichier image. Renvoie son chemin, ou <c>null</c> si
    /// l'utilisateur renonce.
    /// </summary>
    Task<string?> ChoisirImageAsync();

    /// <summary>
    /// Demande un fichier quelconque. <paramref name="filtre"/> suit la
    /// convention Windows : « Libellé|*.ext;*.ext2|Autre libellé|*.* ».
    /// </summary>
    Task<string?> ChoisirFichierAsync(string titre, string filtre);
}
