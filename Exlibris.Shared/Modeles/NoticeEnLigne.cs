namespace Exlibris.Shared.Modeles;

/// <summary>
/// Ce qu'une source publique sait d'un ISBN.
///
/// <b>Volontairement pauvre.</b> Elle ne porte que les champs du formulaire de
/// saisie — ceux qu'on aurait tapés à la main. Y verser tout ce que renvoient
/// les API (identifiants internes, langues, genres BISAC, notes de tirage)
/// obligerait à décider, pour chacun, s'il vaut la peine d'être montré, et la
/// réponse serait presque toujours non.
///
/// Aucun champ n'est garanti : une notice trouvée peut n'avoir qu'un titre.
/// C'est le cas NORMAL pour la bande dessinée francophone, mal couverte par les
/// sources anglophones.
/// </summary>
public class NoticeEnLigne
{
    public string Titre { get; set; } = string.Empty;
    public string Auteurs { get; set; } = string.Empty;
    public string Editeur { get; set; } = string.Empty;
    public int? Annee { get; set; }

    /// <summary>
    /// Le genre annoncé par la source.
    ///
    /// ⚠️ Souvent ANGLAIS — Google Books et Open Library emploient le
    /// vocabulaire BISAC (« Juvenile Fiction », « Comics &amp; Graphic
    /// Novels »). C'est précisément pourquoi le formulaire garde DEUX champs :
    /// celui-ci se remplit tout seul avec ce que dit l'éditeur, et votre propre
    /// genre reste la vôtre.
    /// </summary>
    public string Genre { get; set; } = string.Empty;

    /// <summary>
    /// L'adresse de la couverture chez la source, ou vide.
    ///
    /// Une ADRESSE, pas une image : le téléchargement est une autre affaire, qui
    /// peut échouer seule et qu'on ne veut pas mélanger à la lecture d'une
    /// notice. Ce champ dit seulement où regarder.
    /// </summary>
    public string CouvertureUrl { get; set; } = string.Empty;

    /// <summary>« BnF », « Google Books », « Open Library » — dit ce qu'on peut croire.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Une notice sans titre n'en est pas une : rien à proposer.</summary>
    public bool EstUtilisable => !string.IsNullOrWhiteSpace(Titre);

    /// <summary>
    /// Ce que doit devenir un champ : la valeur en ligne SI le champ est vide,
    /// la valeur actuelle sinon. Renvoie aussi si quelque chose a changé, pour
    /// que le message affiché dise vrai plutôt que d'annoncer des champs qui
    /// n'ont pas bougé.
    ///
    /// <b>La règle du « seulement si vide » est ici, et non dans l'écran</b>,
    /// parce que c'est celle dont l'oubli coûterait le plus cher : vous
    /// cataloguez l'album que vous tenez, la notice décrit une édition qui peut
    /// différer. Un titre corrigé à la main, une année relevée sur la page de
    /// garde ne doivent pas être défaits par un clic — et l'écrasure passerait
    /// inaperçue au milieu de dix champs qui changent ensemble. Placée dans le
    /// ViewModel, elle serait hors de portée des tests, donc invérifiable.
    ///
    /// Pour reprendre la version en ligne d'un champ, on le vide et on relance.
    /// </summary>
    public static (string Valeur, bool Change) Completer(string? actuel, string? propose)
    {
        var enPlace = actuel ?? string.Empty;

        if (string.IsNullOrWhiteSpace(propose) || !string.IsNullOrWhiteSpace(enPlace))
            return (enPlace, false);

        return (propose.Trim(), true);
    }
}
