using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>Les vignettes : sous quelle lettre se range un ouvrage, et dans quel ordre.</summary>
public class VignettesTests
{
    private static Livre L(string titre, string serie = "", int? tome = null) => new() { Titre = titre, SerieNom = serie, NumeroTome = tome };

    [Theory]
    [InlineData("Éclats de vie", "", 'E')]          // sans accent
    [InlineData("1984", "", '#')]                   // les chiffres
    [InlineData("« Ailleurs »", "", 'A')]           // les signes de tête sont passés
    [InlineData("La magicienne trahie", "Thorgal", 'T')]   // la série d'abord, comme le titre complet
    [InlineData("œuvres", "", 'O')]
    public void L_initiale_d_un_titre(string titre, string serie, char attendue) =>
        Assert.Equal(attendue, Vignettes.Initiale(L(titre, serie)));

    [Fact]
    public void Une_fiche_sans_titre_ne_va_que_sous_tout()
    {
        var scannee = new Livre { Isbn = "9782256525018" };
        Assert.Null(Vignettes.Initiale(scannee));
        Assert.Empty(Vignettes.Filtrer([scannee], 'I'));
        Assert.Single(Vignettes.Filtrer([scannee], null));
    }

    [Fact]
    public void Le_filtre_garde_la_lettre_et_trie_sans_accents()
    {
        List<Livre> livres = [L("Zadig"), L("Être et avoir"), L("Ecrire"), L("Alors c'est bien"), L("2001")];

        // Les chiffres d'abord, puis « Ecrire » avant « Être » : l'accent ne compte pas.
        Assert.Equal(["2001", "Alors c'est bien", "Ecrire", "Être et avoir", "Zadig"],
            Vignettes.Filtrer(livres, null).Select(l => l.Titre));
        Assert.Equal(["Ecrire", "Être et avoir"], Vignettes.Filtrer(livres, 'E').Select(l => l.Titre));
        Assert.Equal(["2001"], Vignettes.Filtrer(livres, Vignettes.Chiffres).Select(l => l.Titre));
    }
}
