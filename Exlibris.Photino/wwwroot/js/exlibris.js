// Ex-libris — le seul script maison. Il règle la touche Entrée dans les
// formulaires des dialogues (FormulaireDialogue.razor).
//
// Le geste du WPF : Entrée enregistre (bouton IsDefault), sauf dans le
// commentaire, où elle passe à la ligne. La soumission IMPLICITE du navigateur
// le fait presque — mais elle part aussi quand Entrée sert à choisir une
// suggestion dans une liste ouverte (éditeur, série…) : le formulaire était
// enregistré avec le texte à moitié tapé (« galli » au lieu de « Gallimard »),
// et l'éditeur fantôme créé au passage. Constaté le 04/10/2026.
//
// D'où ce gestionnaire, en phase de CAPTURE pour passer avant tout le monde :
//  - il empêche toujours la soumission implicite ;
//  - si une liste de suggestions est ouverte, il ne fait rien d'autre, et
//    MudBlazor choisit l'élément comme d'habitude (preventDefault n'arrête pas
//    la propagation de l'événement) ;
//  - sinon, il soumet le formulaire lui-même.
// L'état de la liste se lit dans le DOM, AU MOMENT de la frappe : c'est la seule
// lecture qui ne dépende pas de l'ordre d'arrivée des événements côté .NET.
document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
        fermerDialogueDuDessus(e);
        return;
    }

    if (e.key !== 'Enter' || e.shiftKey || e.isComposing) return;

    const formulaire = e.target instanceof Element ? e.target.closest('form.formulaire-dialogue') : null;
    if (!formulaire || e.target.tagName === 'TEXTAREA') return;

    e.preventDefault();

    if (document.querySelector('.mud-popover-open .mud-list-item')) return;

    formulaire.requestSubmit();
}, true);

// Échap ferme le dialogue du dessus — le bouton IsCancel du WPF. L'option
// CloseOnEscapeKey de MudBlazor ne réagit que si le focus est DANS le dialogue,
// ce qui n'est pas garanti sous Photino (vérifié le 04/10/2026 : Échap ne
// fermait ni le formulaire ni l'agrandissement). On clique donc le bouton
// marqué « bouton-annuler » du dernier dialogue ouvert, qui suit exactement le
// même chemin qu'un clic. Une liste de suggestions ouverte garde la priorité :
// Échap la referme d'abord, comme partout.
function fermerDialogueDuDessus(e) {
    if (document.querySelector('.mud-popover-open .mud-list-item')) return;

    const dialogues = document.querySelectorAll('.mud-dialog');
    if (dialogues.length === 0) return;

    const bouton = dialogues[dialogues.length - 1].querySelector('.bouton-annuler');
    if (!bouton) return;

    e.preventDefault();
    e.stopPropagation();
    bouton.click();
}
