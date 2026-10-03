namespace Exlibris.Shared.Modeles;

/// <summary>
/// Une série de bandes dessinées ou de romans. Ce que Lirevia n'a pas, et qui
/// justifie l'application : savoir qu'il manque le tome 12.
/// </summary>
public class Serie
{
    public long Id { get; set; }
    public string Nom { get; set; } = string.Empty;

    /// <summary>
    /// Nombre de tomes que la série COMPTE, quand on le sait. Aucune source
    /// publique ne le donne de façon fiable : il se saisit à la main, sur les
    /// séries qui comptent.
    ///
    /// Sans lui on détecte les trous INTÉRIEURS (on a 1, 2, 3, 5 → il manque le
    /// 4) mais jamais la queue (on a 1 à 20, la série en compte 24) — or c'est
    /// celle-là qui fait une liste de courses.
    /// </summary>
    public int? TomesAnnonces { get; set; }

    /// <summary>Distingue « il me manque la fin » de « la suite n'est pas parue ».</summary>
    public bool Terminee { get; set; }

    public string Note { get; set; } = string.Empty;

    // ----- Calculés par les requêtes de liste
    public int NbPossedes { get; set; }

    /// <summary>Plus haut tome possédé — borne de la détection des trous.</summary>
    public int? TomeMax { get; set; }

    /// <summary>
    /// Les rangs effectivement possédés, renseignés par
    /// <c>ReferentielDepot.Series</c>.
    ///
    /// ⚠️ Le COMPTE ne suffit pas. Posséder quatre tomes d'une série qui en
    /// compte quatre ne dit rien : ce peut être 1, 2, 3, 4 comme 1, 2, 4, 7.
    /// Seuls les numéros permettent de nommer ce qui manque, et c'est le seul
    /// renseignement pour lequel on vient sur cet écran.
    /// </summary>
    public List<int> TomesPossedes { get; set; } = [];

    /// <summary>
    /// Les tomes absents. Recalculé à chaque lecture — quelques centaines de
    /// séries de quelques dizaines de tomes, c'est gratuit — plutôt que retenu
    /// dans un champ : une valeur mise en cache mentirait dès que le nombre
    /// annoncé change, c'est-à-dire exactement au moment où l'on regarde cette
    /// colonne.
    /// </summary>
    public IReadOnlyList<int> Manquants => TomesManquants.Calculer(TomesPossedes, TomesAnnonces);

    public bool Incomplete => Manquants.Count > 0;

    /// <summary>
    /// La série DEMANDE UNE ACTION — c'est ce que filtre l'écran.
    ///
    /// ⚠️ « Rien ne manque » ne veut pas dire « complète » quand on ignore le
    /// nombre de tomes. Défaut constaté le 30/08/2026 : après avoir saisi le
    /// tome 1 de Thorgal, la série n'apparaissait NULLE PART. La plage se borne
    /// au plus haut tome possédé faute d'annonce — le 1 — donc aucun manquant,
    /// donc masquée par le filtre. On ajoute une série, on va la voir, elle n'y
    /// est pas : rien ne distingue cela d'une panne.
    ///
    /// Une série sans nombre annoncé est justement celle sur laquelle on ne
    /// peut RIEN conclure. Elle appelle une saisie, pas un silence — sauf si
    /// l'utilisateur l'a déclarée terminée, auquel cas il a tranché lui-même et
    /// on ne le relance pas.
    /// </summary>
    public bool ATraiter => Incomplete || (TomesAnnonces is null && !Terminee);

    /// <summary>« 4, 7 à 9 », ou un tiret quand il ne manque rien.</summary>
    public string ResumeManquants => Incomplete ? TomesManquants.Resumer(Manquants) : "—";

    /// <summary>
    /// Ce qu'affiche la colonne d'état.
    ///
    /// Quatre états, et chacun dit ce qu'il y a à faire :
    ///
    ///   « N manquants »  — la liste de courses, la raison d'être de l'écran ;
    ///   « Complète »     — vous l'avez toute, et elle est finie ;
    ///   « À renseigner » — on ignore combien elle compte de tomes, donc on ne
    ///                      sait pas s'il en manque. C'est un appel à la saisie,
    ///                      pas un constat ;
    ///   « À jour »       — vous avez tout ce qui est paru, la suite viendra.
    ///
    /// L'ordre des tests n'est pas indifférent. « Complète » se mérite, « À
    /// jour » se constate : une série en cours dont on possède tout ce qui est
    /// paru n'est PAS complète, et le dire ferait manquer le tome suivant à sa
    /// sortie.
    /// </summary>
    public string Statut =>
        Incomplete              ? $"{Manquants.Count} manquant{(Manquants.Count > 1 ? "s" : "")}"
        : Terminee              ? "Complète"
        : TomesAnnonces is null ? "À renseigner"
        : "À jour";
}

public class Auteur
{
    public long Id { get; set; }
    /// <summary>Forme d'affichage : « Rosiński, Grzegorz ».</summary>
    public string Nom { get; set; } = string.Empty;
}

public class Editeur
{
    public long Id { get; set; }
    public string Nom { get; set; } = string.Empty;
}

/// <summary>
/// Le genre au sens de l'édition : « Roman », « Bande dessinée », « Thriller ».
///
/// À ne pas confondre avec <see cref="Classement"/>, et c'est la distinction qui
/// justifie deux listes plutôt qu'une. Le genre dit ce que le livre EST :
/// elle vient de l'éditeur, elle est la même chez tout le monde, et
/// l'enrichissement par ISBN la renseignera tout seul. Le classement dit où VOUS
/// le rangez — « À lire », « Les BD de Paul » — et aucune source ne le devinera.
/// </summary>
public class Genre
{
    public long Id { get; set; }
    public string Nom { get; set; } = string.Empty;
}

/// <summary>
/// À qui appartient l'ouvrage : « moi », « ma femme », « les enfants ».
///
/// Notion FACULTATIVE — voir le réglage « proprietaires.actifs ». Une
/// bibliothèque à un seul propriétaire n'a aucune raison de remplir une case
/// dont la réponse est toujours la même.
/// </summary>
public class Proprietaire
{
    public long Id { get; set; }
    public string Nom { get; set; } = string.Empty;
}

/// <summary>Le rayon, tel que vous le concevez. Un seul par ouvrage.</summary>
public class Classement
{
    public long Id { get; set; }
    public string Nom { get; set; } = string.Empty;

    /// <summary>Ordre d'affichage à la main : l'alphabet n'est pas l'ordre de vos étagères.</summary>
    public int Rang { get; set; }
}

/// <summary>
/// Les rôles d'un auteur sur un ouvrage.
///
/// <b>Indispensable pour la bande dessinée</b> : Van Hamme écrit, Rosiński
/// dessine, et on cherche parfois l'un, parfois l'autre.
///
/// ⚠️ L'import verse TOUT en <see cref="Indetermine"/>. Les sources publiques
/// rendent une chaîne d'auteurs sans dire qui fait quoi ; deviner se tromperait
/// silencieusement, et une erreur de rôle ne se voit pas — elle se découvre en
/// cherchant « tout ce que j'ai de Rosiński » et en n'y trouvant rien. Les rôles
/// se précisent à la main sur les séries qui comptent.
/// </summary>
public static class RoleAuteur
{
    public const string Scenario    = "scenario";
    public const string Dessin      = "dessin";
    public const string Couleur     = "couleur";
    public const string Traduction  = "traduction";

    /// <summary>Rôle par défaut : « on sait que cette personne a participé ».</summary>
    public const string Indetermine = "auteur";

    public static readonly string[] Tous =
        [Scenario, Dessin, Couleur, Traduction, Indetermine];

    public static string Libelle(string role) => role switch
    {
        Scenario   => "Scénario",
        Dessin     => "Dessin",
        Couleur    => "Couleurs",
        Traduction => "Traduction",
        _          => "Auteur"
    };
}
