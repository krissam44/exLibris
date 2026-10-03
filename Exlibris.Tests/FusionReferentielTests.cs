using Dapper;
using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// Le rapprochement des libellés en double, et la mémoire qui le fait tenir.
///
/// <b>Le cas réel qui a motivé tout ceci.</b> Dans une base de vingt-trois
/// ouvrages, le même éditeur figurait quatre fois : « L.G.F - Le livre de
/// poche » (la forme BnF), « le Livre de poche », « LGF », et « French and
/// European Publications Inc, LGF » — cette dernière étant un distributeur
/// américain, artefact de Google Books.
///
/// <b>Aucune clé calculée ne peut les réunir</b>, et c'est vérifié plus bas :
/// celle qui y parviendrait fusionnerait aussi « Gallimard » et « Gallimard
/// Jeunesse ». C'est donc un humain qui tranche — et l'application qui retient
/// sa décision, faute de quoi la notice suivante recréerait ce qu'il vient
/// d'effacer.
/// </summary>
public class FusionReferentielTests : IDisposable
{
    private readonly BancEssai _banc = new();
    private readonly ReferentielDepot _depot;
    private readonly LivreDepot _livres;

    public FusionReferentielTests()
    {
        _depot = new ReferentielDepot(_banc.Fabrique);
        _livres = new LivreDepot(_banc.Fabrique);
    }

    public void Dispose() => _banc.Dispose();

    /// <summary>Les quatre formes relevées dans la base réelle.</summary>
    private static readonly string[] LivreDePoche =
    [
        "L.G.F - Le livre de poche",
        "le Livre de poche",
        "LGF",
        "French and European Publications Inc, LGF",
    ];

    [Fact]
    public void Aucune_cle_ne_rapproche_les_quatre_formes_du_meme_editeur()
    {
        // Le constat qui justifie la fusion manuelle plutôt qu'un repli plus
        // zélé. Si ce test venait à échouer un jour, ce serait que quelqu'un a
        // rendu la clé plus agressive — et il faudrait alors vérifier qu'elle ne
        // réunit pas « Gallimard » et « Gallimard Jeunesse » au passage.
        var cles = LivreDePoche.Select(n => CleReferentiel.Editeur(n)).Distinct().ToList();

        Assert.Equal(4, cles.Count);
    }

    [Fact]
    public void La_cle_editeur_replie_ce_que_la_cle_ordinaire_laissait_passer()
    {
        // La couche automatique : elle ne résout pas le cas ci-dessus, mais elle
        // en évite d'autres, et gratuitement.
        Assert.Equal(
            CleReferentiel.Editeur("Editions 10/18"),
            CleReferentiel.Editeur("10/18"));

        Assert.Equal(
            CleReferentiel.Editeur("Éditions Métailié"),
            CleReferentiel.Editeur("Metailie"));

        // Mais elle reste TIMIDE : un qualificatif distingue toujours deux
        // maisons. C'est la limite qui rend le repli sûr.
        Assert.NotEqual(
            CleReferentiel.Editeur("Gallimard"),
            CleReferentiel.Editeur("Gallimard Jeunesse"));
    }

    [Fact]
    public void Fusionner_deplace_les_ouvrages_vers_le_survivant()
    {
        var survivant = _depot.ResoudreEditeur("le Livre de poche")!.Value;
        var absorbe = _depot.ResoudreEditeur("LGF")!.Value;

        _livres.Creer(new Livre { Titre = "Dune", EditeurId = survivant });
        _livres.Creer(new Livre { Titre = "Fondation", EditeurId = absorbe });

        var deplaces = _depot.Fusionner("editeurs", survivant, [absorbe]);

        Assert.Equal(1, deplaces);
        Assert.Single(_depot.Editeurs());
        Assert.All(_livres.Rechercher(), l => Assert.Equal("le Livre de poche", l.EditeurNom));
    }

    [Fact]
    public void Un_libelle_fusionne_NE_REVIENT_PAS_a_la_notice_suivante()
    {
        // LE test de cette classe. Sans l'alias, la prochaine notice BnF
        // recréerait « L.G.F - Le livre de poche » à côté du survivant, et le
        // ménage serait à refaire tous les mois. C'est ce qui distingue une
        // fusion d'un simple effacement.
        var survivant = _depot.ResoudreEditeur("le Livre de poche")!.Value;
        var absorbe = _depot.ResoudreEditeur("L.G.F - Le livre de poche")!.Value;

        _depot.Fusionner("editeurs", survivant, [absorbe]);

        // La BnF renvoie sa forme habituelle : elle doit retomber sur le
        // survivant, et non créer une cinquième ligne.
        var revenu = _depot.ResoudreEditeur("L.G.F - Le livre de poche");

        Assert.Equal(survivant, revenu);
        Assert.Single(_depot.Editeurs());
    }

    [Fact]
    public void Le_survivant_ne_se_supprime_pas_lui_meme()
    {
        // Un écran qui laisse cocher toute la liste enverra le survivant parmi
        // les absorbés. Le prendre au mot réaffecterait ses ouvrages à
        // lui-même... puis le supprimerait, emportant tout.
        var survivant = _depot.ResoudreEditeur("le Livre de poche")!.Value;
        var absorbe = _depot.ResoudreEditeur("LGF")!.Value;

        _livres.Creer(new Livre { Titre = "Dune", EditeurId = survivant });

        _depot.Fusionner("editeurs", survivant, [survivant, absorbe]);

        Assert.Single(_depot.Editeurs());
        Assert.Equal("le Livre de poche", _livres.Rechercher()[0].EditeurNom);
    }

    [Fact]
    public void Une_seconde_fusion_entraine_les_alias_de_la_premiere()
    {
        // On fusionne A dans B, puis B dans C. L'alias de A pointait vers B, qui
        // n'existe plus : sans reprise, la résolution l'ignorerait et « LGF »
        // reviendrait — la première décision serait silencieusement perdue.
        var a = _depot.ResoudreEditeur("LGF")!.Value;
        var b = _depot.ResoudreEditeur("le Livre de poche")!.Value;
        _depot.Fusionner("editeurs", b, [a]);

        var c = _depot.ResoudreEditeur("Le Livre de Poche jeunesse")!.Value;
        _depot.Fusionner("editeurs", c, [b]);

        Assert.Equal(c, _depot.ResoudreEditeur("LGF"));
        Assert.Single(_depot.Editeurs());
    }

    [Fact]
    public void Fusionner_des_AUTEURS_presents_sur_le_meme_ouvrage_ne_leve_pas()
    {
        // Le seul référentiel qui passe par une table de liaison, dont la clé
        // primaire est (livre_id, auteur_id, role). Deux auteurs du même livre
        // fusionnés produiraient une ligne en double : un UPDATE nu ferait
        // échouer toute l'opération sur ce seul cas.
        var herge = _depot.ResoudreAuteur("Hergé")!.Value;
        var remi = _depot.ResoudreAuteur("Georges Remi")!.Value;

        var id = _livres.Creer(new Livre { Titre = "Le Lotus bleu" });
        _livres.DefinirAuteurs(id, [(herge, RoleAuteur.Indetermine), (remi, RoleAuteur.Indetermine)]);

        _depot.Fusionner("auteurs", herge, [remi]);

        Assert.Single(_depot.Auteurs());
        Assert.Equal("Hergé", _livres.ParId(id)!.Auteurs);
    }

    [Fact]
    public void Le_rattrapage_des_cles_reunit_ce_que_la_nouvelle_regle_reunit()
    {
        // Simule une base d'AVANT le changement de clé : les libellés y sont
        // enregistrés avec l'ancienne clé, celle qui ne repliait pas la
        // ponctuation. « Editions 10/18 » et « 10/18 » y sont donc deux lignes.
        using (var con = _banc.Fabrique.Ouvrir())
            con.Execute("""
                INSERT INTO editeurs (nom, cle_recherche) VALUES
                    ('Editions 10/18', 'editions 10/18'),
                    ('10/18',          '10/18'),
                    ('Actes Sud',      'actes sud');
                """);

        var fusionnes = _depot.RecalculerClesEditeurs();

        Assert.Equal(1, fusionnes);
        Assert.Equal(2, _depot.Editeurs().Count);

        // Et la forme absorbée retombe désormais sur le survivant.
        var noms = _depot.Editeurs().Select(e => e.Nom).ToList();
        Assert.Contains("Actes Sud", noms);
    }

    /// <summary>
    /// ⚠️ LA CASE DOIT PRÉVENIR QUAND ELLE CHANGE.
    ///
    /// L'écran de fusion coche une propriété de la DONNÉE plutôt que de lire la
    /// sélection de la grille — celle-ci restait vide alors que trois lignes
    /// étaient visiblement en surbrillance (30/08/2026). Mais une propriété
    /// muette rouvrirait le même défaut par une autre porte : la grille
    /// afficherait la valeur du chargement, la case se décocherait toute seule
    /// au premier recyclage de la ligne, et le compte affiché ne bougerait
    /// jamais.
    ///
    /// Éprouver un accesseur peut sembler excessif. Ce ne l'est pas ici : la
    /// notification est le SEUL lien entre le clic et ce que l'application en
    /// comprend, et sa disparition ne ferait échouer aucune compilation.
    /// </summary>
    [Fact]
    public void Cocher_une_entree_previent_de_son_changement()
    {
        var entree = new EntreeReferentiel { Nom = "LGF" };
        var annonces = new List<string?>();
        entree.PropertyChanged += (_, e) => annonces.Add(e.PropertyName);

        entree.Choisi = true;
        Assert.Equal([nameof(EntreeReferentiel.Choisi)], annonces);

        // Réécrire la MÊME valeur ne doit rien annoncer : la grille réaffecte
        // ses propriétés à chaque recyclage de ligne, et une annonce à chaque
        // fois ferait recompter le message en pure perte pendant tout le
        // défilement.
        entree.Choisi = true;
        Assert.Single(annonces);

        entree.Choisi = false;
        Assert.Equal(2, annonces.Count);
    }

    [Fact]
    public void Le_rattrapage_est_idempotent()
    {
        _depot.ResoudreEditeur("Casterman");
        _depot.ResoudreEditeur("Dargaud");

        Assert.Equal(0, _depot.RecalculerClesEditeurs());
        Assert.Equal(0, _depot.RecalculerClesEditeurs());
        Assert.Equal(2, _depot.Editeurs().Count);
    }

    [Fact]
    public void Lister_donne_le_nombre_d_ouvrages_de_chaque_entree()
    {
        // Le décompte sert la décision : devant quatre « Livre de poche », on
        // garde celui qui en porte deux cents.
        var poche = _depot.ResoudreEditeur("le Livre de poche")!.Value;
        _depot.ResoudreEditeur("LGF");

        _livres.Creer(new Livre { Titre = "Dune", EditeurId = poche });
        _livres.Creer(new Livre { Titre = "Fondation", EditeurId = poche });

        var entrees = _depot.Lister("editeurs");

        Assert.Equal(2, entrees.Single(e => e.Nom == "le Livre de poche").NbOuvrages);
        Assert.Equal(0, entrees.Single(e => e.Nom == "LGF").NbOuvrages);
    }

    [Fact]
    public void Chaque_referentiel_declare_sa_colonne_etrangere()
    {
        // Garde-fou : un référentiel ajouté sans sa description ferait échouer la
        // fusion à l'exécution, et seulement pour lui.
        Assert.Equal(6, Referentiel.Tous.Count);
        Assert.All(Referentiel.Tous, r =>
        {
            Assert.NotEmpty(r.Table);
            Assert.NotEmpty(r.ColonneEtrangere);
            Assert.NotEmpty(r.TablePorteuse);
        });

        // Les éditeurs sont les seuls à replier la ponctuation.
        Assert.True(Referentiel.Par("editeurs")!.CleEditeur);
        Assert.False(Referentiel.Par("classements")!.CleEditeur);
    }
}
