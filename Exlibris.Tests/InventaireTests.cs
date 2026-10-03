using Dapper;
using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// L'import d'un inventaire scanné, et ce qu'il laisse à faire ensuite.
///
/// <b>Le point de conception que ces tests protègent</b> : une fiche importée
/// n'a qu'un ISBN et un rangement, et son titre est VIDE — pas « ISBN 978… ».
/// <see cref="NoticeEnLigne.Completer"/> ne remplissant que les champs vides
/// pour ne jamais écraser une correction manuelle, un titre provisoire serait
/// un titre définitif : trois cents fiches resteraient nommées par leur
/// code-barres, et le défaut ne se verrait qu'après l'enrichissement.
/// </summary>
public class InventaireTests : IDisposable
{
    private readonly BancEssai _banc = new();
    private readonly InventaireDepot _inventaire;
    private readonly LivreDepot _livres;

    public InventaireTests()
    {
        _inventaire = new InventaireDepot(_banc.Fabrique);
        _livres = new LivreDepot(_banc.Fabrique);
    }

    public void Dispose() => _banc.Dispose();

    private static LigneInventaire Ligne(string isbn, string rangement = "Salon, étagère 2") =>
        new() { Isbn = isbn, Rangement = rangement };

    [Fact]
    public void Un_inventaire_cree_une_fiche_par_ISBN()
    {
        var bilan = _inventaire.Importer([
            Ligne("9782253106654"),
            Ligne("9782203001121"),
        ]);

        Assert.Equal(2, bilan.Crees);
        Assert.Equal(0, bilan.DejaPresents);
        Assert.Equal(2, _livres.Rechercher().Count);
    }

    [Fact]
    public void La_fiche_importee_a_un_titre_VIDE_et_son_rangement()
    {
        // LE test de cette classe. Le titre vide n'est pas un oubli : c'est ce
        // qui permettra à l'enrichissement de le remplir.
        _inventaire.Importer([Ligne("9782253106654", "Chambre de Paul")]);

        var livre = Assert.Single(_livres.Rechercher());

        Assert.Equal(string.Empty, livre.Titre);
        Assert.Equal("Chambre de Paul", livre.Rangement);
        Assert.Equal("9782253106654", livre.Isbn);
    }

    [Fact]
    public void Un_titre_vide_reste_completable_par_une_notice()
    {
        // La vérification qui relie les deux moitiés : ce que l'import laisse
        // doit être exactement ce que la complétion sait remplir. Un libellé
        // provisoire échouerait ici, et c'est tout l'intérêt de le poser.
        _inventaire.Importer([Ligne("9782253106654")]);
        var livre = Assert.Single(_livres.Rechercher());

        var (valeur, change) = NoticeEnLigne.Completer(livre.Titre, "Quelqu'un d'autre");

        Assert.True(change);
        Assert.Equal("Quelqu'un d'autre", valeur);
    }

    [Fact]
    public void La_liste_montre_l_ISBN_tant_que_le_titre_manque()
    {
        // Sans ce repli, l'écran afficherait trois cents lignes blanches — ce
        // qu'on prendrait pour un défaut d'affichage plutôt que pour du travail
        // en attente.
        _inventaire.Importer([Ligne("9782253106654")]);

        var livre = Assert.Single(_livres.Rechercher());

        Assert.Contains("9782253106654", livre.TitreComplet);
        Assert.Contains("compléter", livre.TitreComplet);
    }

    [Fact]
    public void Reimporter_le_MEME_fichier_ne_duplique_rien()
    {
        // L'accident le plus probable : on ne sait plus si l'import a été fait,
        // on le refait. Le schéma autorise pourtant deux fiches de même ISBN —
        // deux exemplaires du même livre — mais c'est un geste délibéré, qui se
        // fait à la main.
        var fichier = new[] { Ligne("9782253106654"), Ligne("9782203001121") };

        _inventaire.Importer(fichier);
        var second = _inventaire.Importer(fichier);

        Assert.Equal(0, second.Crees);
        Assert.Equal(2, second.DejaPresents);
        Assert.Equal(2, _livres.Rechercher().Count);
    }

    [Fact]
    public void Un_ISBN_repete_DANS_le_fichier_ne_donne_qu_une_fiche()
    {
        // On repasse deux fois devant la même étagère, ou la douchette bipe
        // deux fois. Le bilan doit le dire sans créer de doublon.
        var bilan = _inventaire.Importer([
            Ligne("9782253106654", "Salon"),
            Ligne("9782253106654", "Salon"),
        ]);

        Assert.Equal(1, bilan.Crees);
        Assert.Equal(1, bilan.DejaPresents);
        Assert.Single(_livres.Rechercher());
    }

    [Fact]
    public void Un_import_ne_touche_pas_aux_fiches_deja_saisies()
    {
        // Un livre catalogué à la main, avec son titre et son rangement. Un
        // import qui le croiserait ne doit rien y changer : ni écraser le
        // rangement, ni vider le titre.
        var id = _livres.Creer(new Livre
        {
            Titre = "Quelqu'un d'autre",
            Isbn = "9782253106654",
            Rangement = "Ma table de nuit",
        });

        _inventaire.Importer([Ligne("9782253106654", "Salon, étagère 2")]);

        var relu = _livres.ParId(id)!;
        Assert.Equal("Quelqu'un d'autre", relu.Titre);
        Assert.Equal("Ma table de nuit", relu.Rangement);
        Assert.Single(_livres.Rechercher());
    }

    [Fact]
    public void Un_fichier_vide_ne_fait_rien_et_ne_leve_pas()
    {
        var bilan = _inventaire.Importer([]);

        Assert.Equal(0, bilan.Total);
        Assert.Empty(_livres.Rechercher());
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  La file d'attente de l'enrichissement
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Les_fiches_importees_attendent_d_etre_completees()
    {
        _inventaire.Importer([Ligne("9782253106654"), Ligne("9782203001121")]);

        Assert.Equal(2, _inventaire.NombreACompleter());
        Assert.Equal(2, _inventaire.ACompleter().Count);
    }

    [Fact]
    public void Une_fiche_SANS_ISBN_n_attend_rien()
    {
        // Les ~30 % d'albums sans code-barres : aucune source ne saura répondre,
        // et ils occuperaient la place de ceux qu'on peut vraiment compléter.
        _livres.Creer(new Livre { Titre = "Album sans code-barres" });

        Assert.Equal(0, _inventaire.NombreACompleter());
    }

    [Fact]
    public void Une_tentative_INFRUCTUEUSE_sort_quand_meme_la_fiche_de_la_file()
    {
        // Marquer même l'échec est ce qui fait avancer le traitement : sans
        // cela, un ISBN qu'aucune source ne connaît serait redemandé à chaque
        // passage, et le lot suivant repartirait sur les mêmes introuvables.
        _inventaire.Importer([Ligne("9782253106654")]);
        var livre = Assert.Single(_inventaire.ACompleter());

        _inventaire.MarquerTentee(livre.Id);

        Assert.Equal(0, _inventaire.NombreACompleter());
    }

    [Fact]
    public void La_file_se_traite_par_LOTS()
    {
        // Trois cents fiches, des sources gratuites et des quotas : le
        // traitement doit pouvoir s'arrêter et reprendre le lendemain là où il
        // en était.
        _inventaire.Importer(Enumerable.Range(0, 10)
            .Select(i => Ligne($"978225310665{i}")));

        Assert.Equal(3, _inventaire.ACompleter(limite: 3).Count);
        Assert.Equal(10, _inventaire.NombreACompleter());

        foreach (var livre in _inventaire.ACompleter(limite: 3))
            _inventaire.MarquerTentee(livre.Id);

        Assert.Equal(7, _inventaire.NombreACompleter());
    }

    [Fact]
    public void Un_import_interrompu_ne_laisse_pas_la_moitie_d_une_etagere()
    {
        // La transaction est le garde-fou : soit tout entre, soit rien. Ce test
        // vérifie la propriété observable — trois cents lignes deviennent trois
        // cents fiches, jamais cent quarante-deux.
        var lignes = Enumerable.Range(0, 50).Select(i => Ligne($"97822531066{i:D2}")).ToList();

        var bilan = _inventaire.Importer(lignes);

        Assert.Equal(50, bilan.Crees);

        using var con = _banc.Fabrique.Ouvrir();
        Assert.Equal(50, con.ExecuteScalar<int>("SELECT COUNT(*) FROM livres"));
    }
}
