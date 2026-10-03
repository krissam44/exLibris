using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// Le silence du scan à la volée : un livre laissé devant l'objectif ne se
/// signale qu'une fois, un nouveau livre toujours.
/// </summary>
public class DernierCodeLuTests
{
    [Fact]
    public void Le_premier_code_n_est_pas_une_repetition()
    {
        Assert.False(new DernierCodeLu().EstLeMeme("9782253106654"));
    }

    [Fact]
    public void Le_meme_livre_encore_devant_l_objectif_est_une_repetition()
    {
        // LE cas : l'anti-rafale réaccepte au bout de trois secondes, et le
        // livre est toujours là.
        var dernier = new DernierCodeLu();

        Assert.False(dernier.EstLeMeme("9782253106654"));
        Assert.True(dernier.EstLeMeme("9782253106654"));
        Assert.True(dernier.EstLeMeme(" 9782253106654 "));
    }

    [Fact]
    public void Revenir_a_un_livre_apres_un_autre_le_signale_de_nouveau()
    {
        // Un geste, pas un écho : on a présenté un autre livre entre-temps.
        var dernier = new DernierCodeLu();

        dernier.EstLeMeme("9782253106654");
        Assert.False(dernier.EstLeMeme("9782070368228"));
        Assert.False(dernier.EstLeMeme("9782253106654"));
    }

    [Fact]
    public void Oublier_rend_la_parole_au_meme_livre()
    {
        // Après un toucher délibéré ou une saisie à la main.
        var dernier = new DernierCodeLu();

        dernier.EstLeMeme("9782253106654");
        dernier.Oublier();

        Assert.False(dernier.EstLeMeme("9782253106654"));
    }

    [Fact]
    public void Un_code_vide_n_efface_pas_le_precedent()
    {
        var dernier = new DernierCodeLu();

        dernier.EstLeMeme("9782253106654");

        Assert.False(dernier.EstLeMeme(""));
        Assert.False(dernier.EstLeMeme(null));
        Assert.True(dernier.EstLeMeme("9782253106654"));
    }
}
