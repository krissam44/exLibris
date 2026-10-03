using System.ComponentModel;

namespace Exlibris.Shared.Modeles;

/// <summary>
/// Décrit un référentiel : sa table, ce qui pointe vers lui, et la façon dont
/// ses libellés se rapprochent.
///
/// <b>Une seule description pour trois usages</b> — la résolution à la saisie,
/// la fusion à la main, et l'écran qui les liste. Trois endroits qui
/// connaîtraient séparément le nom de la colonne étrangère finiraient par
/// diverger, et la divergence ne se verrait qu'au moment d'une fusion : les
/// ouvrages resteraient rattachés au libellé supprimé, donc sans éditeur, en
/// silence.
/// </summary>
public sealed record Referentiel(
    string Table,
    string Libelle,
    string TablePorteuse,
    string ColonneEtrangere,
    bool CleEditeur = false)
{
    /// <summary>
    /// La clé de rapprochement d'un libellé de ce référentiel.
    ///
    /// Les éditeurs ont droit à un repli plus large que les autres : leur nom
    /// se ponctue et se préfixe de mille façons selon la source — « Editions
    /// 10/18 » et « 10/18 », « Éditions Métailié » et « Metailie ». Un rayon
    /// personnel, lui, s'écrit comme on l'a tapé, et replier sa ponctuation ne
    /// rapprocherait rien qui doive l'être.
    /// </summary>
    public string Cle(string? nom) =>
        CleEditeur ? CleReferentiel.Editeur(nom) : CleReferentiel.CleCollation(nom);

    /// <summary>
    /// Les six référentiels, dans l'ordre où l'écran les propose.
    ///
    /// Les éditeurs viennent en tête parce que ce sont eux qui se dédoublent le
    /// plus : chaque source les nomme à sa façon, là où une série ou un rayon
    /// est saisi une fois par une seule personne.
    /// </summary>
    public static readonly IReadOnlyList<Referentiel> Tous =
    [
        new("editeurs",      "Éditeurs",       "livres", "editeur_id", CleEditeur: true),
        new("genres",        "Genres",         "livres", "genre_id"),
        // « Mes catégories », et non « Mes genres » : c'est tout le sujet du
        // renommage. Le mot de l'éditeur est « genre » ; celui que vous posez
        // vous-même reste une catégorie, et les deux se distinguent désormais
        // par le mot lui-même plutôt que par un qualificatif.
        new("classements",   "Mes catégories", "livres", "classement_id"),
        new("proprietaires", "Propriétaires",  "livres", "proprietaire_id"),
        new("series",        "Séries",         "livres", "serie_id"),

        // ⚠️ Les auteurs sont les seuls à passer par une table de liaison, et
        // sa clé primaire porte (livre_id, auteur_id, role). Fusionner deux
        // auteurs sur un même ouvrage peut donc produire un doublon de clé —
        // voir ReferentielDepot.Fusionner, qui l'absorbe au lieu d'échouer.
        new("auteurs",       "Auteurs",        "livre_auteurs", "auteur_id"),
    ];

    public static Referentiel? Par(string table) =>
        Tous.FirstOrDefault(r => r.Table == table);
}

/// <summary>Une entrée de référentiel, avec ce qui s'y rattache.</summary>
public class EntreeReferentiel : INotifyPropertyChanged
{
    public long Id { get; set; }
    public string Nom { get; set; } = string.Empty;

    /// <summary>
    /// Combien d'ouvrages la portent. Affiché à côté du libellé parce que c'est
    /// ce qui permet de choisir le survivant d'une fusion en connaissance de
    /// cause — garder celui qui en porte deux cents plutôt que celui qui en
    /// porte un.
    /// </summary>
    public int NbOuvrages { get; set; }

    /// <summary>
    /// Cochée pour la fusion.
    ///
    /// ⚠️ L'ÉTAT VIT SUR LA DONNÉE, et non dans la sélection de la grille. Le
    /// premier essai s'appuyait sur <c>SfDataGrid.SelectedItems</c> : le bouton
    /// « Réunir » répondait « sélectionnez au moins deux entrées » alors que
    /// trois lignes étaient visiblement en surbrillance. Vérifié le 30/08/2026.
    ///
    /// Une case cochée n'a pas ce problème, et vaut mieux ici pour deux raisons
    /// qui tiennent à l'usage : elle SE VOIT — on relit son choix avant de
    /// lancer une opération sans retour — et elle SURVIT au tri comme au filtre,
    /// là où une sélection multiple se perd au premier clic distrait.
    ///
    /// La notification est indispensable : sans elle la grille afficherait la
    /// valeur du chargement et la case reviendrait se décocher toute seule au
    /// moindre rafraîchissement de la ligne.
    /// </summary>
    public bool Choisi
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Choisi)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
