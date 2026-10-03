using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// Le filtre des rafales de la caméra.
///
/// <b>Défaut constaté sur un téléphone réel le 30/08/2026</b> : en détection
/// continue, un livre présenté une ou deux secondes remonte le même code cinq à
/// vingt fois. Le fichier restait juste — la déduplication par ISBN tenait —
/// mais le journal se remplissait de « déjà relevé », au moment précis où l'on
/// compte dessus pour savoir où l'on en est.
///
/// L'instant est fourni par le test : la règle s'éprouve sans attendre trois
/// secondes à chaque cas.
/// </summary>
public class AntiRafaleTests
{
    private static readonly DateTime T = new(2026, 8, 30, 14, 0, 0, DateTimeKind.Local);

    [Fact]
    public void Le_premier_passage_est_accepte()
    {
        var filtre = new AntiRafale();

        Assert.True(filtre.Accepte("9782253106654", T));
    }

    [Fact]
    public void Les_echos_immediats_sont_ignores()
    {
        // LE cas : vingt lectures en deux secondes, pour un seul livre présenté.
        var filtre = new AntiRafale();

        Assert.True(filtre.Accepte("9782253106654", T));

        for (var i = 1; i <= 20; i++)
            Assert.False(filtre.Accepte("9782253106654", T.AddMilliseconds(i * 100)));
    }

    [Fact]
    public void Le_MEME_livre_represente_plus_tard_est_de_nouveau_accepte()
    {
        // Le geste légitime qu'un refus définitif casserait : on repasse un
        // livre devant la caméra pour vérifier qu'il a bien été pris. Ce second
        // passage doit répondre — c'est l'inventaire qui dira qu'il y était
        // déjà.
        var filtre = new AntiRafale();

        Assert.True(filtre.Accepte("9782253106654", T));
        Assert.True(filtre.Accepte("9782253106654", T.AddSeconds(10)));
    }

    [Fact]
    public void Un_livre_laisse_devant_l_objectif_ne_prolonge_pas_son_silence()
    {
        // ⚠️ Le piège de conception. Repousser l'échéance à chaque écho ferait
        // qu'un livre posé devant la caméra ne serait JAMAIS réaccepté, le
        // silence se prolongeant de trois secondes en trois secondes. Ici, les
        // échos se succèdent pendant quatre secondes, et le passage à
        // T + 3,5 s doit quand même être accepté.
        var filtre = new AntiRafale();

        Assert.True(filtre.Accepte("9782253106654", T));

        for (var ms = 200; ms <= 2800; ms += 200)
            Assert.False(filtre.Accepte("9782253106654", T.AddMilliseconds(ms)));

        Assert.True(filtre.Accepte("9782253106654", T.AddMilliseconds(3500)));
    }

    [Fact]
    public void Deux_livres_differents_ne_se_genent_pas()
    {
        // On enchaîne les ouvrages : le silence porte sur UN code, pas sur la
        // caméra. Un filtre global ferait perdre le livre suivant pris trop
        // vite sur l'étagère.
        var filtre = new AntiRafale();

        Assert.True(filtre.Accepte("9782253106654", T));
        Assert.True(filtre.Accepte("9782203001121", T.AddMilliseconds(300)));
        Assert.True(filtre.Accepte("2246245125", T.AddMilliseconds(600)));

        // Et chacun garde son propre silence.
        Assert.False(filtre.Accepte("9782253106654", T.AddMilliseconds(900)));
    }

    [Fact]
    public void Un_code_vide_n_est_jamais_accepte()
    {
        var filtre = new AntiRafale();

        Assert.False(filtre.Accepte(null, T));
        Assert.False(filtre.Accepte("", T));
        Assert.False(filtre.Accepte("   ", T));
    }

    [Fact]
    public void La_table_ne_gonfle_pas_sur_une_longue_seance()
    {
        // Trois cents ouvrages en une heure : la mémoire du filtre ne doit pas
        // croître avec le nombre de livres relevés.
        var filtre = new AntiRafale();

        for (var i = 0; i < 300; i++)
            Assert.True(filtre.Accepte($"978225310{i:D4}", T.AddSeconds(i * 10)));

        // Le dernier reste protégé DANS sa fenêtre de silence : le nettoyage
        // n'emporte que le périmé, jamais ce qui sert encore. Une seconde après
        // son passage, donc, et non dix — au-delà, le silence a expiré et
        // l'acceptation serait normale.
        Assert.False(filtre.Accepte("9782253100299", T.AddSeconds(299 * 10 + 1)));
    }

    [Fact]
    public void Le_silence_est_reglable()
    {
        // Pour qu'un usage plus lent — une étagère haute, un escabeau — puisse
        // l'allonger sans toucher au reste.
        var filtre = new AntiRafale(TimeSpan.FromSeconds(10));

        Assert.True(filtre.Accepte("9782253106654", T));
        Assert.False(filtre.Accepte("9782253106654", T.AddSeconds(5)));
        Assert.True(filtre.Accepte("9782253106654", T.AddSeconds(11)));
    }

    [Fact]
    public void Une_reinitialisation_rend_la_parole_au_dernier_code()
    {
        // L'appui sur « Scanner » : on RÉCLAME une lecture. Le silence de trois
        // secondes vaut contre les échos d'une caméra laissée en marche, pas
        // contre un geste délibéré — le même livre représenté aussitôt doit
        // répondre, ne serait-ce que « déjà relevé ».
        var filtre = new AntiRafale();

        Assert.True(filtre.Accepte("9782253106654", T));
        Assert.False(filtre.Accepte("9782253106654", T.AddMilliseconds(200)));

        filtre.Reinitialiser();

        Assert.True(filtre.Accepte("9782253106654", T.AddMilliseconds(400)));
    }

    [Fact]
    public void Apres_reinitialisation_le_filtre_protege_de_nouveau()
    {
        // Oublier n'est pas se taire : la rafale qui suit doit être filtrée
        // comme avant, sans quoi l'écran redeviendrait illisible dès le premier
        // livre du nouvel armement.
        var filtre = new AntiRafale();

        filtre.Accepte("9782253106654", T);
        filtre.Reinitialiser();

        Assert.True(filtre.Accepte("9782253106654", T.AddMilliseconds(400)));
        Assert.False(filtre.Accepte("9782253106654", T.AddMilliseconds(600)));
    }
}
