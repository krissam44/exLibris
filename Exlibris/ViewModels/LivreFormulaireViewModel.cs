using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.ViewModels;

/// <summary>
/// La saisie d'un ouvrage — le chemin des ~1 500 albums sans code-barres.
///
/// <b>Ce formulaire doit être rapide au clavier</b>, pas complet. Seul le titre
/// est obligatoire ; tout le reste se remplit si on le sait. Les listes
/// d'éditeurs, de séries et de classements sont ouvertes à la saisie libre : ce
/// qu'on tape et qui n'existe pas encore est créé à l'enregistrement, sans
/// détour par un écran de paramétrage.
/// </summary>
public partial class LivreFormulaireViewModel : ObservableObject
{
    private readonly ReferentielDepot _referentiels;
    private readonly NoticeEnLigneService _notices;
    private readonly CouvertureDepot _couvertures;
    private readonly Services.IDialogueService _dialogues;

    /// <summary>
    /// Le livre tel qu'il était à l'ouverture, ou <c>null</c> pour un nouvel
    /// ouvrage. Gardé pour <see cref="VersLivre"/> : voir pourquoi là-bas.
    /// </summary>
    private readonly Livre? _existant;

    public LivreFormulaireViewModel(
        ReferentielDepot referentiels, NoticeEnLigneService notices,
        CouvertureDepot couvertures, Services.IDialogueService dialogues,
        bool proprietairesActifs, Livre? existant = null)
    {
        _referentiels = referentiels;
        _notices = notices;
        _couvertures = couvertures;
        _dialogues = dialogues;
        _existant = existant;
        ProprietairesActifs = proprietairesActifs;

        Editeurs    = referentiels.Editeurs().Select(e => e.Nom).ToList();
        Series      = referentiels.Series().Select(s => s.Nom).ToList();
        Genres  = referentiels.Genres().Select(c => c.Nom).ToList();
        Classements = referentiels.Classements().Select(c => c.Nom).ToList();
        Rangements  = referentiels.Rangements();
        Proprietaires = referentiels.Proprietaires().Select(p => p.Nom).ToList();

        if (existant is null) return;

        Id           = existant.Id;
        Isbn         = existant.Isbn;
        Titre        = existant.Titre;
        Serie        = existant.SerieNom;
        NumeroTome   = existant.NumeroTome;
        Auteurs      = existant.Auteurs;
        Editeur      = existant.EditeurNom;
        Genre    = existant.GenreNom;
        Classement   = existant.ClassementNom;
        Proprietaire = existant.ProprietaireNom;
        Rangement    = existant.Rangement;
        Annee        = existant.Annee;
        Lu           = existant.Lu;
        Appreciation = existant.Appreciation;
        Commentaire  = existant.Commentaire;
        Couverture   = existant.Couverture;
        CheminCouverture = _couvertures.Chemin(Couverture);
    }

    public long Id { get; }

    public IReadOnlyList<string> Editeurs { get; }
    public IReadOnlyList<string> Series { get; }
    public IReadOnlyList<string> Genres { get; }
    public IReadOnlyList<string> Classements { get; }
    public IReadOnlyList<string> Rangements { get; }
    public IReadOnlyList<string> Proprietaires { get; }

    /// <summary>
    /// Commande l'affichage du champ « Propriétaire ». Reçu du dehors plutôt
    /// que lu ici : le formulaire s'ouvre des dizaines de fois par séance de
    /// catalogage, et relire le réglage en base à chaque ouverture serait
    /// autant d'allers-retours pour une valeur qui ne change pas pendant une
    /// saisie.
    /// </summary>
    public bool ProprietairesActifs { get; }

    [ObservableProperty] private string _isbn = string.Empty;
    [ObservableProperty] private string _titre = string.Empty;
    [ObservableProperty] private string _serie = string.Empty;
    [ObservableProperty] private int? _numeroTome;

    /// <summary>
    /// Auteurs séparés par une virgule, tels qu'on les dicte : « Van Hamme,
    /// Rosiński ». Le rôle n'est pas demandé ICI — il se précise plus tard, sur
    /// les séries qui comptent. Réclamer « scénario ou dessin ? » à chaque
    /// saisie ralentirait les 1 500 albums pour un renseignement qu'on ne
    /// cherchera que sur quelques-uns.
    /// </summary>
    [ObservableProperty] private string _auteurs = string.Empty;

    [ObservableProperty] private string _editeur = string.Empty;

    /// <summary>
    /// Le genre au sens de l'édition. Distinct du classement : celui-ci dit ce
    /// que le livre EST, celui-là où vous le rangez. La liste est amorcée d'une
    /// nomenclature courte et reste ouverte à la saisie.
    /// </summary>
    [ObservableProperty] private string _genre = string.Empty;

    [ObservableProperty] private string _classement = string.Empty;
    [ObservableProperty] private string _rangement = string.Empty;

    /// <summary>À qui appartient l'ouvrage. Ignoré si le réglage est éteint.</summary>
    [ObservableProperty] private string _proprietaire = string.Empty;
    [ObservableProperty] private int? _annee;
    [ObservableProperty] private bool _lu;
    [ObservableProperty] private int? _appreciation;
    [ObservableProperty] private string _commentaire = string.Empty;

    /// <summary>Message affiché sous le titre quand la saisie ne convient pas.</summary>
    [ObservableProperty] private string _erreur = string.Empty;

    /// <summary>
    /// Avertissement d'ISBN douteux. Il SIGNALE, il ne bloque pas : un album
    /// réellement sur l'étagère doit pouvoir entrer même si son code-barres est
    /// illisible ou fantaisiste.
    /// </summary>
    [ObservableProperty] private string _avertissement = string.Empty;

    /// <summary>
    /// « Ma catégorie » part de celle de l'éditeur, tant qu'elle est VIDE.
    ///
    /// C'est le geste que décrit l'utilisateur : l'éditeur annonce « Policier »,
    /// on préfère « Thriller ». La proposition arrive donc déjà remplie, et se
    /// corrige en tapant par-dessus.
    ///
    /// La condition est tout le sujet. Recopier systématiquement écraserait un
    /// choix fait à la main — « Les BD de Paul » redeviendrait « Bande
    /// dessinée » au moindre passage sur la fiche, et la perte serait
    /// silencieuse. En ne remplissant que le vide, la saisie rapide y gagne
    /// (deux mille romans classés « Roman ») sans jamais défaire une décision.
    /// </summary>
    partial void OnGenreChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(Classement))
            Classement = value;
    }

    /// <summary>
    /// Efface le reproche dès que la saisie le répare.
    ///
    /// Sans cela, « Le titre est obligatoire » restait sous un titre
    /// parfaitement rempli jusqu'à la validation suivante : le formulaire
    /// accusait l'utilisateur d'une faute qu'il venait de corriger.
    /// </summary>
    partial void OnTitreChanged(string value)
    {
        if (Erreur.Length > 0 && !string.IsNullOrWhiteSpace(value))
            Erreur = string.Empty;
    }

    partial void OnIsbnChanged(string value) => Avertissement =
        string.IsNullOrWhiteSpace(value) || Shared.Modeles.Isbn.EstValide(value)
            ? string.Empty
            : "Ce code-barres ne tombe pas juste — vérifiez la saisie. L'enregistrement reste possible.";

    /// <summary>Le nom du fichier image, tel qu'il sera rangé en base.</summary>
    public string Couverture { get; private set; } = string.Empty;

    /// <summary>
    /// Le chemin COMPLET de l'image, pour l'affichage — nul tant qu'il n'y a
    /// rien à montrer.
    ///
    /// Deux propriétés pour une seule image, parce que ce sont deux choses : la
    /// base range un nom de fichier, qui reste valable si le dossier déménage ;
    /// l'écran a besoin d'un chemin, qui n'a de sens que sur cette machine.
    /// Ranger le chemin complet en base rendrait la bibliothèque incopiable.
    /// </summary>
    [ObservableProperty] private string? _cheminCouverture;

    /// <summary>
    /// La couverture en grand, au clic sur la vignette.
    ///
    /// L'image conservée fait plusieurs centaines de pixels de côté — celle de
    /// la BnF, 600 × 966 — alors que la vignette en montre 132. L'agrandissement
    /// ne fabrique donc rien : il montre ce qu'on a déjà, et c'est ce qui permet
    /// de vérifier qu'on tient la bonne édition avant d'enregistrer.
    /// </summary>
    [RelayCommand]
    private async Task AgrandirCouvertureAsync()
    {
        if (CheminCouverture is { Length: > 0 } chemin)
            await _dialogues.AfficherImageAsync(string.IsNullOrWhiteSpace(Titre) ? "Couverture" : Titre, chemin);
    }

    /// <summary>
    /// Prend une couverture sur le disque.
    ///
    /// Le chemin de secours, et il est loin d'être marginal : aucune source en
    /// ligne ne connaît les ~30 % d'albums sans code-barres, donc aucune ne
    /// rendra leur couverture. Une photo prise au téléphone, ou une image
    /// enregistrée depuis un navigateur, feront l'affaire.
    ///
    /// Contrairement à la recherche par ISBN, celle-ci REMPLACE une couverture
    /// existante : c'est un geste explicite, sur ce seul champ, et l'utilisateur
    /// voit immédiatement le résultat.
    /// </summary>
    [RelayCommand]
    private async Task ChoisirCouvertureAsync()
    {
        if (await _dialogues.ChoisirImageAsync() is not { } source) return;

        var fichier = await _couvertures.ImporterAsync(source);

        if (fichier.Length == 0)
        {
            MessageRecherche = "Ce fichier n'est pas une image que je sache lire.";
            return;
        }

        Couverture = fichier;
        CheminCouverture = _couvertures.Chemin(fichier);
        MessageRecherche = "Couverture reprise depuis votre disque.";
    }

    /// <summary>Ce que la recherche par ISBN a donné, dit à côté du bouton.</summary>
    [ObservableProperty] private string _messageRecherche = string.Empty;

    /// <summary>
    /// Vrai pendant l'appel réseau : le bouton se grise, faute de quoi une
    /// double frappe lancerait deux recherches concurrentes dont la plus lente
    /// écraserait le résultat de l'autre.
    /// </summary>
    [ObservableProperty] private bool _rechercheEnCours;

    /// <summary>
    /// Interroge les sources publiques et remplit ce qui manque.
    ///
    /// <b>Ne remplit QUE les champs vides.</b> C'est la même règle que le
    /// pré-remplissage de le genre, et elle vaut ici encore davantage :
    /// vous cataloguez l'album que vous tenez, la notice décrit une édition qui
    /// peut différer. Un titre corrigé à la main, une année relevée sur la page
    /// de garde ne doivent pas être défaits par un clic — d'autant qu'une
    /// écrasure passerait inaperçue au milieu de dix champs qui changent.
    /// La règle elle-même vit dans <see cref="NoticeEnLigne.Completer"/>, où un
    /// test peut l'atteindre.
    /// </summary>
    [RelayCommand]
    private async Task RechercherAsync()
    {
        if (string.IsNullOrWhiteSpace(Isbn))
        {
            MessageRecherche = "Saisissez d'abord un ISBN.";
            return;
        }

        RechercheEnCours = true;
        MessageRecherche = "Recherche en cours…";

        try
        {
            var notice = await _notices.ChercherAsync(Isbn);

            if (notice is null)
            {
                // Formulation délibérée : « rien trouvé », et non « erreur ».
                // Une source injoignable et un ISBN inconnu se ressemblent du
                // point de vue de la saisie, et dans les deux cas la suite est
                // la même — taper à la main. C'est le cas ORDINAIRE pour la
                // bande dessinée francophone, mal couverte par ces sources.
                MessageRecherche = "Aucune notice trouvée — à saisir à la main.";
                return;
            }

            var remplis = new List<string>();

            if (Poser(notice.Titre, Titre, v => Titre = v)) remplis.Add("titre");
            if (Poser(notice.Auteurs, Auteurs, v => Auteurs = v)) remplis.Add("auteurs");
            if (Poser(notice.Editeur, Editeur, v => Editeur = v)) remplis.Add("éditeur");
            if (Poser(notice.Genre, Genre, v => Genre = v)) remplis.Add("genre");

            if (notice.Annee is { } annee && Annee is null)
            {
                Annee = annee;
                remplis.Add("année");
            }

            // La couverture ne s'ajoute que s'il n'y en a pas déjà une : même
            // règle que les champs texte, et pour la même raison — une image
            // choisie ou déjà rapportée ne doit pas être remplacée en silence.
            if (Couverture.Length == 0 && notice.CouvertureUrl.Length > 0)
            {
                var fichier = await _couvertures.TelechargerAsync(notice.CouvertureUrl);
                if (fichier.Length > 0)
                {
                    Couverture = fichier;
                    CheminCouverture = _couvertures.Chemin(fichier);
                    remplis.Add("couverture");
                }
            }

            MessageRecherche = remplis.Count == 0
                ? $"{notice.Source} : rien à ajouter, tout était déjà rempli."
                : $"{notice.Source} : {string.Join(", ", remplis)}.";
        }
        finally
        {
            RechercheEnCours = false;
        }
    }

    /// <summary>Applique la règle de complétion à un champ de l'écran.</summary>
    private static bool Poser(string propose, string actuel, Action<string> ecrire)
    {
        var (valeur, change) = NoticeEnLigne.Completer(actuel, propose);
        if (change) ecrire(valeur);
        return change;
    }

    /// <summary>
    /// Contrôle avant fermeture. Renvoie <c>false</c> pour laisser le dialogue
    /// ouvert, l'utilisateur devant sa correction.
    /// </summary>
    public bool Valide()
    {
        Erreur = string.IsNullOrWhiteSpace(Titre) ? "Le titre est obligatoire." : string.Empty;
        return Erreur.Length == 0;
    }

    /// <summary>
    /// Construit le livre à enregistrer, en résolvant les référentiels au
    /// passage — ce qui les crée si besoin.
    ///
    /// <b>Les champs que le formulaire n'affiche pas sont REPRIS du livre
    /// d'origine.</b> <c>LivreDepot.MettreAJour</c> écrit toutes les colonnes :
    /// un livre bâti sur les seuls champs de l'écran effaçait donc le résumé,
    /// le nombre de pages, le format, la date de lecture, la source de la
    /// notice et la date d'enrichissement à chaque modification — corriger une
    /// faute dans le titre renvoyait la fiche dans « À compléter » et perdait
    /// le résumé de la BnF, en silence. Constaté le 04/10/2026, couvert par
    /// LivreFormulaireTests. Un champ AJOUTÉ un jour au modèle sans être montré
    /// ici doit être reporté de la même façon.
    /// </summary>
    public Livre VersLivre() => new()
    {
        NbPages      = _existant?.NbPages,
        Format       = _existant?.Format ?? string.Empty,
        Resume       = _existant?.Resume ?? string.Empty,
        DateLecture  = _existant?.DateLecture,
        SourceNotice = _existant?.SourceNotice ?? string.Empty,
        EnrichiLe    = _existant?.EnrichiLe,

        Id           = Id,
        Isbn         = Isbn,
        Titre        = Titre.Trim(),
        SerieId      = _referentiels.ResoudreSerie(Serie),
        NumeroTome   = NumeroTome,
        EditeurId    = _referentiels.ResoudreEditeur(Editeur),
        GenreId  = _referentiels.ResoudreGenre(Genre),
        ClassementId = _referentiels.ResoudreClassement(Classement),

        // Rien n'est résolu quand le réglage est éteint. Sans cette garde,
        // désactiver l'option laisserait la valeur d'une saisie précédente
        // s'écrire encore — invisible à l'écran, donc impossible à corriger.
        ProprietaireId = ProprietairesActifs
            ? _referentiels.ResoudreProprietaire(Proprietaire)
            : null,
        Rangement    = Rangement.Trim(),
        Annee        = Annee,
        Lu           = Lu,
        Appreciation = Appreciation,
        Commentaire  = Commentaire,
        Couverture   = Couverture,
    };

    /// <summary>
    /// Les auteurs saisis, résolus en identifiants. Découpés sur la virgule et
    /// le point-virgule, tous en rôle indéterminé — voir
    /// <see cref="RoleAuteur.Indetermine"/>.
    /// </summary>
    public List<(long AuteurId, string Role)> AuteursResolus() =>
        Auteurs.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Select(nom => _referentiels.ResoudreAuteur(nom))
               .Where(id => id is not null)
               .Select(id => (id!.Value, RoleAuteur.Indetermine))
               .ToList();
}
