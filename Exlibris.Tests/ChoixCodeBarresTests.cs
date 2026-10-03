using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// Le tri des codes-barres qu'une image remonte.
///
/// <b>Défaut constaté en librairie le 31/08/2026</b> : un taux important de
/// non-lectures sur des codes-barres parfaitement nets, là où une autre
/// application lisait tout. Le scanner ne regardait que le PREMIER résultat de
/// chaque image ; quand la caméra voyait d'abord l'étiquette de prix du
/// libraire, l'EAN du livre — présent dans la même image — était jeté avec
/// elle.
///
/// D'où les deux règles éprouvées ici : parcourir TOUS les résultats, et savoir
/// reconnaître un livre plutôt qu'un code-barres quelconque.
/// </summary>
public class ChoixCodeBarresTests
{
    // Le roman de la photo du 31/08/2026, celui qui n'a pas été lu.
    private const string Livre = "9782073096456";

    // Un EAN-13 de grande consommation : clé de contrôle juste, préfixe 301 —
    // exactement ce qu'une étiquette de rayon présente à l'objectif.
    private const string Conserve = "3017620422003";

    [Fact]
    public void Le_code_du_livre_est_retenu_meme_s_il_n_est_pas_le_premier()
    {
        // LE cas de la librairie : la caméra voit d'abord l'étiquette de prix.
        Assert.Equal(Livre, ChoixCodeBarres.PremierLivre([Conserve, Livre]));
    }

    [Fact]
    public void Une_etiquette_seule_ne_donne_rien()
    {
        Assert.Equal(string.Empty, ChoixCodeBarres.PremierLivre([Conserve]));
    }

    [Fact]
    public void Le_premier_livre_l_emporte_sur_le_suivant()
    {
        // Deux livres dans l'image — celui du dessous, sur un comptoir. On en
        // prend un seul, et c'est celui que la caméra a vu en premier.
        Assert.Equal(Livre, ChoixCodeBarres.PremierLivre([Livre, "9782253106654"]));
    }

    [Fact]
    public void Le_code_additionnel_de_prix_est_retire()
    {
        // Certaines lectures concatènent le supplément à cinq chiffres.
        Assert.Equal(Livre, ChoixCodeBarres.PremierLivre([Livre + "01234"]));
    }

    [Fact]
    public void Une_cle_de_controle_fausse_est_refusee()
    {
        Assert.Equal(string.Empty, ChoixCodeBarres.PremierLivre(["9782073096450"]));
    }

    [Fact]
    public void Une_liste_vide_ou_absente_ne_leve_pas()
    {
        Assert.Equal(string.Empty, ChoixCodeBarres.PremierLivre(null));
        Assert.Equal(string.Empty, ChoixCodeBarres.PremierLivre([]));
        Assert.Equal(string.Empty, ChoixCodeBarres.PremierLivre([null, "", "   "]));
    }

    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Un_ean_valide_hors_bookland_n_est_pas_un_livre()
    {
        // La distinction qui fait tout le tri : la clé de contrôle ne dit RIEN
        // du contenu. Sans le préfixe, une conserve entrerait au relevé.
        Assert.True(Isbn.EstValide(Conserve));
        Assert.False(Isbn.EstLivre(Conserve));
    }

    [Theory]
    [InlineData("9782073096456")] // 978 — l'immense majorité des livres
    [InlineData("9791032913970")] // 979 — la plage ouverte depuis 2013
    [InlineData("0306406152")]    // ISBN-10, sans préfixe : saisi à la main
    public void Un_livre_est_reconnu(string code)
    {
        Assert.True(Isbn.EstLivre(code));
    }
}
