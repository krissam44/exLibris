namespace Exlibris.Shared.Modeles;

/// <summary>
/// Un livre posé sur l'étagère.
///
/// <b>Une ligne = un objet physique</b>, et non une œuvre abstraite. La
/// séparation œuvre/exemplaire de Lirevia existe parce que la fiche y est
/// partagée entre bibliothèques et que l'exemplaire s'y prête ; ni l'un ni
/// l'autre ici. Deux exemplaires du même album — deux cadeaux identiques —
/// sont donc deux lignes portant le même ISBN.
/// </summary>
public class Livre
{
    public long Id { get; set; }

    /// <summary>
    /// Vide pour les ~30 % d'albums sans code-barres. Écrit en base comme
    /// <c>NULL</c>, jamais comme chaîne vide : sinon tous ces albums se
    /// ressembleraient au contrôle de doublon.
    /// </summary>
    public string Isbn { get; set; } = string.Empty;

    public string Titre { get; set; } = string.Empty;

    public long? SerieId { get; set; }

    /// <summary>
    /// Numéro dans la série. <c>null</c> pour les hors-séries, intégrales et
    /// « tome 0 » : ils ne participent pas à la numérotation, et ne doivent donc
    /// pas compter dans la détection des trous.
    /// </summary>
    public int? NumeroTome { get; set; }

    public long? EditeurId { get; set; }

    /// <summary>Le genre éditeur : ce que le livre EST.</summary>
    public long? GenreId { get; set; }

    /// <summary>Le rayon personnel : où VOUS le rangez.</summary>
    public long? ClassementId { get; set; }

    /// <summary>
    /// À qui il appartient. <c>null</c> dans la plupart des bibliothèques : le
    /// champ ne s'affiche que si le réglage correspondant est activé.
    /// </summary>
    public long? ProprietaireId { get; set; }

    /// <summary>Emplacement physique, texte libre : « Salle 1, étagère B ».</summary>
    public string Rangement { get; set; } = string.Empty;

    public int? Annee { get; set; }
    public int? NbPages { get; set; }
    public string Format { get; set; } = string.Empty;
    public string Resume { get; set; } = string.Empty;

    /// <summary>Chemin RELATIF au dossier `couvertures/` voisin de la base.</summary>
    public string Couverture { get; set; } = string.Empty;

    // ----- Ce qui n'appartient qu'à une bibliothèque personnelle
    public bool Lu { get; set; }
    public DateTime? DateLecture { get; set; }

    /// <summary>1 à 5, <c>null</c> tant qu'on n'a pas d'avis.</summary>
    public int? Appreciation { get; set; }

    public string Commentaire { get; set; } = string.Empty;

    // ----- Traçabilité
    /// <summary>« BnF », « Google Books », « Open Library », ou vide si saisi à la main.</summary>
    public string SourceNotice { get; set; } = string.Empty;

    /// <summary>
    /// Dernière TENTATIVE d'enrichissement, réussie ou non. C'est ce qui rend le
    /// traitement par lot reprenable d'un jour sur l'autre sans redemander aux
    /// sources ce qu'elles ont déjà refusé.
    /// </summary>
    public DateTime? EnrichiLe { get; set; }

    public DateTime CreeLe { get; set; }
    public DateTime? ModifieLe { get; set; }

    // ----- Champs de JOINTURE, renseignés par les requêtes de liste.
    //       Non persistés : ils viennent des référentiels.
    public string SerieNom { get; set; } = string.Empty;
    public string EditeurNom { get; set; } = string.Empty;
    public string GenreNom { get; set; } = string.Empty;
    public string ClassementNom { get; set; } = string.Empty;
    public string ProprietaireNom { get; set; } = string.Empty;

    /// <summary>Auteurs concaténés pour l'affichage en liste (« Van Hamme, Rosiński »).</summary>
    public string Auteurs { get; set; } = string.Empty;

    /// <summary>
    /// Ce qu'on affiche en colonne « Titre » : le titre seul pour un roman, mais
    /// « Thorgal — 7. La magicienne trahie » pour un album. Une série de vingt
    /// tomes triée sur le seul titre est illisible.
    /// </summary>
    public string TitreComplet =>
        string.IsNullOrWhiteSpace(Titre) ? SansTitre
        : string.IsNullOrWhiteSpace(SerieNom) ? Titre
        : NumeroTome is null
            ? $"{SerieNom} — {Titre}"
            : $"{SerieNom} — {NumeroTome}. {Titre}";

    /// <summary>
    /// Ce qu'on affiche d'une fiche importée dont le titre n'est pas encore
    /// connu.
    ///
    /// Une fiche scannée n'a QUE son ISBN tant que l'enrichissement n'est pas
    /// passé — son titre est vide, et il doit le rester pour que la complétion
    /// puisse le remplir. Sans ce repli, la liste montrerait trois cents lignes
    /// blanches, qu'on prendrait pour un défaut d'affichage plutôt que pour du
    /// travail en attente.
    /// </summary>
    private string SansTitre =>
        string.IsNullOrWhiteSpace(Isbn) ? "(sans titre)" : $"ISBN {Isbn} — à compléter";
}
