using Dapper;
using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// Le propriétaire d'un ouvrage, et le réglage qui commande son existence.
///
/// <b>La notion est FACULTATIVE</b>, parce que l'application est destinée à
/// être distribuée : une bibliothèque à un seul propriétaire n'a pas à remplir
/// une case dont la réponse est toujours la même. Ce qui vaut d'être éprouvé
/// est donc autant le réglage que le champ — un défaut par défaut mal choisi
/// imposerait la colonne à tout le monde.
/// </summary>
public class ProprietaireTests : IDisposable
{
    private readonly BancEssai _banc = new();
    private readonly LivreDepot _depot;
    private readonly ReferentielDepot _referentiels;
    private readonly ParametreDepot _parametres;

    public ProprietaireTests()
    {
        _depot = new LivreDepot(_banc.Fabrique);
        _referentiels = new ReferentielDepot(_banc.Fabrique);
        _parametres = new ParametreDepot(_banc.Fabrique);
    }

    public void Dispose() => _banc.Dispose();

    [Fact]
    public void Le_proprietaire_survit_a_l_aller_retour_en_base()
    {
        var id = _depot.Creer(new Livre
        {
            Titre = "Quelqu'un d'autre",
            ProprietaireId = _referentiels.ResoudreProprietaire("Ma femme"),
            ClassementId = _referentiels.ResoudreClassement("Thriller"),
        });

        var relu = _depot.ParId(id)!;

        // Les deux ensemble : la jointure du propriétaire a été écrite en
        // recopiant celle du classement, et un alias laissé identique rendrait
        // le même nom dans les deux champs sans lever la moindre erreur.
        Assert.Equal("Ma femme", relu.ProprietaireNom);
        Assert.Equal("Thriller", relu.ClassementNom);
    }

    [Fact]
    public void Un_ouvrage_sans_proprietaire_reste_parfaitement_valide()
    {
        // Le cas NORMAL — celui de toutes les bibliothèques qui n'activent pas
        // l'option. La colonne doit rester nulle sans rien casser.
        var id = _depot.Creer(new Livre { Titre = "Dune" });

        var relu = _depot.ParId(id)!;
        Assert.Null(relu.ProprietaireId);
        Assert.Equal(string.Empty, relu.ProprietaireNom);
    }

    [Fact]
    public void Deux_proprietaires_se_distinguent_dans_la_liste()
    {
        _depot.Creer(new Livre
        {
            Titre = "Dune",
            ProprietaireId = _referentiels.ResoudreProprietaire("Moi"),
        });
        _depot.Creer(new Livre
        {
            Titre = "Quelqu'un d'autre",
            ProprietaireId = _referentiels.ResoudreProprietaire("Ma femme"),
        });

        var livres = _depot.Rechercher();

        Assert.Equal(2, _referentiels.Proprietaires().Count);
        Assert.Equal("Moi", livres.Single(l => l.Titre == "Dune").ProprietaireNom);
        Assert.Equal("Ma femme", livres.Single(l => l.Titre == "Quelqu'un d'autre").ProprietaireNom);
    }

    [Fact]
    public void La_liste_des_proprietaires_est_vide_tant_que_personne_n_en_saisit()
    {
        // Contrairement aux genres, AUCUN amorçage : « Moi » et « Ma femme »
        // ne conviendraient pas à tout le monde, et une liste pré-remplie de
        // valeurs inadaptées est plus pénible qu'une liste vide.
        Assert.Empty(_referentiels.Proprietaires());
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  Le réglage
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Le_reglage_est_ETEINT_par_defaut()
    {
        // LE test de cette classe. L'application est destinée à être distribuée :
        // le célibataire qui l'installe ne doit pas trouver une case
        // « Propriétaire » dont il n'a que faire. Un défaut à `true` imposerait
        // la notion à tous ceux qui n'ont rien demandé.
        Assert.False(_parametres.LireBooleen(ParametreDepot.ProprietairesActifs));
    }

    [Fact]
    public void Le_reglage_se_retient_d_une_lecture_a_l_autre()
    {
        _parametres.EcrireBooleen(ParametreDepot.ProprietairesActifs, true);
        Assert.True(_parametres.LireBooleen(ParametreDepot.ProprietairesActifs));

        _parametres.EcrireBooleen(ParametreDepot.ProprietairesActifs, false);
        Assert.False(_parametres.LireBooleen(ParametreDepot.ProprietairesActifs));
    }

    [Fact]
    public void Ecrire_deux_fois_le_meme_reglage_ne_cree_pas_deux_lignes()
    {
        // La clé est primaire, donc un INSERT répété lèverait. C'est l'UPSERT
        // qui l'évite, et c'est exactement le cas courant : on coche, on
        // décoche, on recoche.
        _parametres.EcrireBooleen(ParametreDepot.ProprietairesActifs, true);
        _parametres.EcrireBooleen(ParametreDepot.ProprietairesActifs, false);
        _parametres.EcrireBooleen(ParametreDepot.ProprietairesActifs, true);

        using var con = _banc.Fabrique.Ouvrir();
        Assert.Equal(1L, con.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM parametres WHERE cle = @cle",
            new { cle = ParametreDepot.ProprietairesActifs }));
    }

    [Fact]
    public void Une_valeur_incomprehensible_rend_le_defaut_et_non_false()
    {
        // Une base bricolée à la main, ou un réglage écrit par une version
        // future. Rendre `false` d'office serait juste ici, mais faux le jour où
        // un réglage aura `true` pour défaut — il se retrouverait désactivé sans
        // que personne l'ait demandé.
        _parametres.Ecrire("essai.defaut", "peut-être");

        Assert.True(_parametres.LireBooleen("essai.defaut", defaut: true));
        Assert.False(_parametres.LireBooleen("essai.defaut", defaut: false));
    }

    [Fact]
    public void Un_reglage_jamais_ecrit_rend_le_defaut_demande()
    {
        Assert.True(_parametres.LireBooleen("essai.inconnu", defaut: true));
        Assert.False(_parametres.LireBooleen("essai.inconnu", defaut: false));
    }
}
