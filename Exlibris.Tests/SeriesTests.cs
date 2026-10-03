using Dapper;
using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// Les tomes manquants — la règle pour laquelle Ex-libris existe.
///
/// La première moitié éprouve la fonction PURE, sans base ni fenêtre : c'est là
/// que vivent les décisions (où commence la plage, où elle s'arrête, quand on
/// refuse de conclure). La seconde vérifie qu'elle reçoit bien les rangs
/// possédés — un calcul juste alimenté par une requête muette rendrait « rien ne
/// manque » sur toute une bibliothèque.
/// </summary>
public class TomesManquantsTests
{
    [Fact]
    public void Un_trou_interieur_est_signale()
    {
        var manquants = TomesManquants.Calculer([1, 2, 3, 5], null);

        Assert.Equal([4], manquants);
    }

    /// <summary>
    /// Le cas que le nombre annoncé rend possible, et le seul qui produise une
    /// liste de courses : la SUITE d'une série, que rien dans les tomes possédés
    /// ne laisse deviner.
    /// </summary>
    [Fact]
    public void La_queue_d_une_serie_n_apparait_qu_avec_le_nombre_annonce()
    {
        Assert.Empty(TomesManquants.Calculer([1, 2, 3], null));
        Assert.Equal([4, 5], TomesManquants.Calculer([1, 2, 3], 5));
    }

    /// <summary>
    /// ⚠️ La décision la plus lourde de conséquences. Partir du plus petit tome
    /// possédé aurait déclaré cette série complète — le silence le plus coûteux
    /// de l'écran, puisqu'on vient précisément y chercher ce qui manque.
    /// </summary>
    [Fact]
    public void La_plage_commence_a_1_et_non_au_plus_petit_tome_possede()
    {
        var manquants = TomesManquants.Calculer([5, 6, 7], null);

        Assert.Equal([1, 2, 3, 4], manquants);
    }

    /// <summary>
    /// Une intégrale, un recueil, un hors-série : rien n'y autorise à raisonner
    /// sur des rangs. Sans ce refus, la règle précédente répondrait « il vous
    /// manque les tomes 1 à 24 » à quelqu'un qui possède l'œuvre entière.
    /// </summary>
    [Fact]
    public void Sans_aucun_tome_numerote_on_ne_conclut_rien()
    {
        Assert.Empty(TomesManquants.Calculer([], 24));
    }

    /// <summary>
    /// Nombre annoncé DÉPASSÉ : la série a continué de paraître, ou le nombre a
    /// été saisi de mémoire. On possède le tome 26 d'une série annoncée à 24.
    ///
    /// La borne prend le plus GRAND des deux. S'en tenir au nombre annoncé
    /// arrêterait la plage à 24 et perdrait le tome 25 — un trou, entre deux
    /// volumes qu'on a en main, purement et simplement invisible.
    ///
    /// ⚠️ Ce jeu de valeurs a été choisi APRÈS coup. Le premier — 1, 3, 25 pour
    /// 24 annoncés — donnait le même résultat avec les deux formules, le tome au
    /// delà de l'annonce ne bouchant aucun trou : le test passait dans les deux
    /// cas, et ne prouvait rien. Il faut un ÉCART entre l'annonce et le plus
    /// haut tome possédé pour que la différence existe.
    /// </summary>
    [Fact]
    public void Un_tome_au_dela_du_nombre_annonce_etend_la_plage()
    {
        var manquants = TomesManquants.Calculer([1, 2, 26], 24);

        Assert.Contains(25, manquants);             // perdu si la borne s'arrête à 24
        Assert.Equal(23, manquants.Count);          // 3 à 25
        Assert.DoesNotContain(26, manquants);
    }

    [Fact]
    public void Les_doublons_ne_comptent_qu_une_fois()
    {
        // Deux exemplaires du même album : c'est permis, et cela ne doit pas
        // faire croire qu'on possède un tome de plus.
        Assert.Empty(TomesManquants.Calculer([1, 1, 2], null));
    }

    [Theory]
    [InlineData(new[] { 4 }, "4")]
    [InlineData(new[] { 8, 9 }, "8 et 9")]
    [InlineData(new[] { 7, 8, 9 }, "7 à 9")]
    [InlineData(new[] { 4, 7, 8, 9, 21, 22, 23, 24 }, "4, 7 à 9, 21 à 24")]
    public void Le_resume_regroupe_les_plages(int[] manquants, string attendu)
    {
        Assert.Equal(attendu, TomesManquants.Resumer(manquants));
    }
}

public class SerieDepotTests : IDisposable
{
    private readonly BancEssai _banc = new();

    public void Dispose() => _banc.Dispose();

    private ReferentielDepot Depot => new(_banc.Fabrique);

    /// <summary>
    /// ⚠️ LE TEST QUI COMPTE. Le calcul a beau être juste, il ne vaut que par ce
    /// qu'on lui donne : la requête agrège par série, et un GROUP BY ne rend pas
    /// les valeurs qu'il a repliées. Sans la seconde requête, chaque série
    /// arriverait avec une liste de rangs VIDE — donc « aucun tome numéroté »,
    /// donc « rien ne manque » sur toute la bibliothèque. Aucune erreur, aucun
    /// message : un écran vert de bout en bout.
    /// </summary>
    [Fact]
    public void Les_rangs_possedes_remontent_avec_la_serie()
    {
        var serie = CreerSerie("Thorgal");
        AjouterTome(serie, 1);
        AjouterTome(serie, 2);
        AjouterTome(serie, 5);

        var lue = Assert.Single(Depot.Series());

        Assert.Equal([1, 2, 5], lue.TomesPossedes.Order());
        Assert.Equal([3, 4], lue.Manquants);
        Assert.Equal("3 et 4", lue.ResumeManquants);
    }

    /// <summary>
    /// Les hors-séries et intégrales sont à NULL en base. Comptés comme des
    /// tomes, ils gonfleraient le nombre possédé sans jamais boucher de trou.
    /// </summary>
    [Fact]
    public void Un_ouvrage_sans_numero_ne_participe_pas_a_la_numerotation()
    {
        var serie = CreerSerie("Blake et Mortimer");
        AjouterTome(serie, 1);
        AjouterTome(serie, null);   // une intégrale

        var lue = Assert.Single(Depot.Series());

        Assert.Equal([1], lue.TomesPossedes);
        Assert.Empty(lue.Manquants);
    }

    [Fact]
    public void Le_nombre_annonce_et_le_drapeau_termine_se_conservent()
    {
        var serie = CreerSerie("XIII");
        AjouterTome(serie, 1);

        Assert.True(Depot.MettreAJourSerie(serie, 27, terminee: true, "coffret offert"));

        var lue = Assert.Single(Depot.Series());

        Assert.Equal(27, lue.TomesAnnonces);
        Assert.True(lue.Terminee);
        Assert.Equal("coffret offert", lue.Note);
        Assert.Equal(26, lue.Manquants.Count);      // 2 à 27
    }

    /// <summary>
    /// Zéro vaut « je ne sais pas », et non « série de zéro tome ». Sans ce
    /// repli, une frappe malheureuse serait indiscernable de l'ignorance : la
    /// borne prenant le plus grand des deux, l'affichage ne changerait pas et
    /// personne ne s'apercevrait que le renseignement a été perdu.
    /// </summary>
    [Fact]
    public void Zero_tome_annonce_vaut_je_ne_sais_pas()
    {
        var serie = CreerSerie("Lanfeust");
        AjouterTome(serie, 1);

        Depot.MettreAJourSerie(serie, 0, terminee: false, string.Empty);

        Assert.Null(Assert.Single(Depot.Series()).TomesAnnonces);
    }

    /// <summary>
    /// ⚠️ LE CAS THORGAL — 30/08/2026, signalé depuis l'application.
    ///
    /// On saisit le tome 1 d'une série neuve, sans en connaître le nombre de
    /// volumes. La plage se borne alors au plus haut tome possédé, donc aucun
    /// manquant, donc le filtre « à compléter » la masquait : la série
    /// n'apparaissait NULLE PART, le lendemain de sa création. Rien ne
    /// distinguait cela d'une panne.
    ///
    /// Une série dont on ignore le nombre de tomes est justement celle sur
    /// laquelle on ne peut rien conclure. Elle appelle une saisie.
    /// </summary>
    [Fact]
    public void Une_serie_sans_nombre_de_tomes_reste_a_traiter()
    {
        var thorgal = CreerSerie("Thorgal");
        AjouterTome(thorgal, 1);

        var lue = Assert.Single(Depot.Series());

        Assert.Empty(lue.Manquants);        // rien ne manque, au sens strict
        Assert.True(lue.ATraiter);          // et pourtant elle doit s'afficher
        Assert.Equal("À renseigner", lue.Statut);
    }

    /// <summary>
    /// Le revers : l'utilisateur qui a coché « terminée » a tranché lui-même.
    /// Le relancer pour un nombre qu'il juge inutile ferait de l'écran une
    /// liste de reproches qu'on ne peut pas faire taire.
    /// </summary>
    [Fact]
    public void Une_serie_declaree_terminee_ne_reclame_plus_de_nombre()
    {
        var serie = CreerSerie("Un one-shot classé en série");
        AjouterTome(serie, 1);
        Depot.MettreAJourSerie(serie, null, terminee: true, string.Empty);

        var lue = Assert.Single(Depot.Series());

        Assert.False(lue.ATraiter);
        Assert.Equal("Complète", lue.Statut);
    }

    /// <summary>
    /// « Complète » se mérite, « À jour » se constate — et c'est toute la raison
    /// d'être du drapeau. Confondre les deux ferait manquer le tome suivant à sa
    /// sortie.
    /// </summary>
    [Fact]
    public void Une_serie_en_cours_dont_on_a_tout_est_a_jour_et_non_complete()
    {
        var enCours = CreerSerie("Une série en cours");
        AjouterTome(enCours, 1);
        AjouterTome(enCours, 2);

        // ⚠️ Le nombre annoncé est INDISPENSABLE à ce test, et son absence l'a
        // fait échouer quand « À renseigner » est apparu — à raison. Posséder
        // les tomes 1 et 2 d'une série dont on ignore la longueur n'autorise pas
        // à se dire à jour : on n'a aucune idée de ce qui est paru depuis. Ce
        // n'est « à jour » que parce qu'on sait qu'elle en compte deux.
        Depot.MettreAJourSerie(enCours, 2, terminee: false, string.Empty);

        var finie = CreerSerie("Une série achevée");
        AjouterTome(finie, 1);
        Depot.MettreAJourSerie(finie, 1, terminee: true, string.Empty);

        var series = Depot.Series().ToDictionary(s => s.Nom);

        Assert.Equal("À jour", series["Une série en cours"].Statut);
        Assert.Equal("Complète", series["Une série achevée"].Statut);
    }

    // ----- La série passe par le VRAI chemin de création : c'est lui qui calcule
    //       la clé de recherche, et un INSERT écrit ici en dupliquerait la
    //       règle — donc finirait par en diverger.
    private long CreerSerie(string nom) => Depot.ResoudreSerie(nom)!.Value;

    private void AjouterTome(long serieId, int? numero)
    {
        using var con = _banc.Fabrique.Ouvrir();
        con.Execute("""
            INSERT INTO livres (titre, cle_recherche, serie_id, numero_tome, cree_le)
            VALUES (@titre, @cle, @serieId, @numero, @maintenant)
            """,
            new
            {
                titre = $"Tome {numero?.ToString() ?? "hors-série"}",
                cle = "t",
                serieId,
                numero,
                maintenant = DateTime.UtcNow.ToString("o"),
            });
    }
}
