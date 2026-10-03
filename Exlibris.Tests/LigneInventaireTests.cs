using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// Le format du fichier d'inventaire, celui que le scanner écrit et
/// qu'Ex-libris importe.
///
/// <b>C'est un CONTRAT entre deux applications</b>, et c'est ce qui justifie
/// une suite à part. Les deux côtés se déploient séparément — le téléphone
/// n'est pas mis à jour le même jour que le poste — et une divergence ne se
/// verrait pas à l'import : elle produirait des rangements tronqués, ou des
/// fiches sans emplacement, découverts des semaines plus tard devant une
/// étagère.
/// </summary>
public class LigneInventaireTests
{
    private static LigneInventaire Ligne(string isbn, string rangement = "") =>
        new() { Isbn = isbn, Rangement = rangement };

    [Fact]
    public void Ce_qui_est_ecrit_se_relit_a_l_identique()
    {
        // Le test qui compte : l'aller-retour complet, sur les deux colonnes.
        var depart = new[]
        {
            Ligne("9782253106654", "Salon, étagère 2"),
            Ligne("9782203001121", "Chambre de Paul"),
        };

        var relues = LigneInventaire.Analyser(LigneInventaire.Composer(depart));

        Assert.Equal(2, relues.Count);
        Assert.Equal("9782253106654", relues[0].Isbn);
        Assert.Equal("Salon, étagère 2", relues[0].Rangement);
        Assert.Equal("Chambre de Paul", relues[1].Rangement);
    }

    [Fact]
    public void Un_rangement_contenant_le_separateur_survit_a_l_aller_retour()
    {
        // « Salon; étagère 2 » est un rangement parfaitement légitime. Sans
        // protection, il produirait trois colonnes au lieu de deux — donc un
        // rangement tronqué à « Salon », en silence.
        var relues = LigneInventaire.Analyser(
            LigneInventaire.Composer([Ligne("9782253106654", "Salon; étagère 2")]));

        Assert.Equal("Salon; étagère 2", Assert.Single(relues).Rangement);
    }

    [Fact]
    public void Un_rangement_contenant_des_guillemets_survit_aussi()
    {
        const string rangement = "Bibliothèque \"du fond\"";

        var relues = LigneInventaire.Analyser(
            LigneInventaire.Composer([Ligne("9782253106654", rangement)]));

        Assert.Equal(rangement, Assert.Single(relues).Rangement);
    }

    [Fact]
    public void L_en_tete_n_est_pas_pris_pour_un_livre()
    {
        var contenu = LigneInventaire.Composer([Ligne("9782253106654")]);

        Assert.StartsWith(LigneInventaire.Entete, contenu);
        Assert.Single(LigneInventaire.Analyser(contenu));
    }

    [Fact]
    public void Un_fichier_relu_par_Excel_reste_lisible()
    {
        // Excel réenregistre volontiers en ajoutant des colonnes vides et en
        // changeant la casse de l'en-tête. Le fichier passe par ses mains dès
        // qu'on veut y jeter un œil : il doit revenir intact.
        var relues = LigneInventaire.Analyser("""
            ISBN;RANGEMENT;
            9782253106654;Salon, étagère 2;
            9782203001121;;
            """);

        Assert.Equal(2, relues.Count);
        Assert.Equal("Salon, étagère 2", relues[0].Rangement);
        Assert.Equal(string.Empty, relues[1].Rangement);
    }

    [Fact]
    public void Les_ISBN_sont_normalises_a_la_lecture()
    {
        // Une douchette rend treize chiffres, mais un code recopié à la main
        // porte ses tirets. Les deux doivent produire la même fiche à l'import,
        // sinon le même livre entre deux fois.
        var relues = LigneInventaire.Analyser("""
            isbn;rangement
            978-2-253-10665-4;Salon
             9782253106654 ;Salon
            """);

        Assert.Equal(2, relues.Count);
        Assert.All(relues, l => Assert.Equal("9782253106654", l.Isbn));
    }

    [Fact]
    public void Les_lignes_vides_et_sans_ISBN_sont_ecartees_sans_bruit()
    {
        // Un fichier se termine presque toujours par une ligne vide. Refuser
        // d'importer trois cents livres pour cela serait absurde.
        var relues = LigneInventaire.Analyser("""
            isbn;rangement
            9782253106654;Salon

            ;Chambre
            """);

        Assert.Equal("9782253106654", Assert.Single(relues).Isbn);
    }

    [Fact]
    public void Un_contenu_vide_ne_rend_rien_et_ne_leve_pas()
    {
        // Un fichier choisi par erreur, ou un partage interrompu. À l'appelant
        // de constater le zéro et de le dire ; ici, pas d'exception.
        Assert.Empty(LigneInventaire.Analyser(null));
        Assert.Empty(LigneInventaire.Analyser(""));
        Assert.Empty(LigneInventaire.Analyser("   \r\n  "));
        Assert.Empty(LigneInventaire.Analyser("n'importe quoi\nvraiment"));
    }

    [Fact]
    public void Un_rangement_multiligne_ne_casse_pas_le_fichier()
    {
        // La saisie sur téléphone laisse passer un retour à la ligne collé
        // depuis ailleurs. Il romprait le format en deux lignes dont la seconde
        // n'aurait pas d'ISBN : l'ouvrage garderait un rangement tronqué.
        var relues = LigneInventaire.Analyser(
            LigneInventaire.Composer([Ligne("9782253106654", "Salon\r\nétagère 2")]));

        Assert.Single(relues);
        Assert.DoesNotContain('\n', relues[0].Rangement);
        Assert.Contains("étagère 2", relues[0].Rangement);
    }
}
