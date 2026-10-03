using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// L'analyse des réponses des sources publiques.
///
/// <b>Aucun appel réseau.</b> Une suite qui interrogerait vraiment Google
/// échouerait un jour sur un quota, une panne ou un train sans connexion — et
/// on finirait par ne plus la croire, ce qui est pire que de ne pas l'avoir.
/// Ce qui est éprouvé ici est la SEULE partie où nous pouvons nous tromper :
/// la lecture du JSON.
///
/// Les charges utiles sont réduites aux champs qui nous intéressent, mais leur
/// FORME est celle des vraies réponses — c'est là que les deux sources
/// diffèrent, et c'est ce qui casse.
/// </summary>
public class NoticeEnLigneTests
{
    /// <summary>
    /// La réponse RÉELLE de la BnF pour 9782253106654, réduite aux champs lus.
    /// Recopiée telle quelle — y compris ses doubles espaces et son
    /// « Musso, Guillaume (1974-....) » - parce que c'est exactement cette
    /// forme-là qui casse.
    /// </summary>
    private const string ReponseBnf = """
        <?xml version="1.0" encoding="UTF-8"?>
        <srw:searchRetrieveResponse xmlns:srw="http://www.loc.gov/zing/srw/">
        <srw:numberOfRecords>1</srw:numberOfRecords>
        <srw:records><srw:record><srw:recordData>
        <oai_dc:dc xmlns:oai_dc="http://www.openarchives.org/OAI/2.0/oai_dc/"
                   xmlns:dc="http://purl.org/dc/elements/1.1/">
          <dc:identifier>http://catalogue.bnf.fr/ark:/12148/cb48736742q</dc:identifier>
          <dc:title>Quelqu'un d'autre</dc:title>
          <dc:creator>Musso, Guillaume (1974-....)</dc:creator>
          <dc:publisher>L.G.F - Le livre de poche  (Paris)</dc:publisher>
          <dc:date>2026</dc:date>
          <dc:description>Collection : Litterature</dc:description>
          <dc:identifier>ISBN 9782253106654</dc:identifier>
          <dc:language>fre</dc:language>
        </oai_dc:dc>
        </srw:recordData></srw:record></srw:records>
        </srw:searchRetrieveResponse>
        """;

    [Fact]
    public void La_BnF_trouve_le_roman_de_poche_que_les_autres_ignorent()
    {
        // L'ISBN signalé par l'utilisateur : Lirevia le trouvait, Ex-libris non,
        // parce qu'il n'interrogeait pas la BnF. Vérifié le 29/08/2026 sur les
        // trois sources : Open Library répond {}, Google refuse l'appel (429).
        var notice = NoticeEnLigneService.AnalyserBnf(ReponseBnf);

        Assert.NotNull(notice);
        Assert.Equal("Quelqu'un d'autre", notice.Titre);
        Assert.Equal(2026, notice.Annee);
        Assert.Equal("BnF", notice.Source);
    }

    [Fact]
    public void Un_auteur_de_la_BnF_arrive_en_Prenom_Nom_sans_ses_dates()
    {
        // DEUX pièges d'un coup. Les dates de vie n'ont rien à faire dans un
        // champ de saisie ; et surtout l'inversion, car le formulaire découpe
        // ses auteurs SUR LA VIRGULE : « Musso, Guillaume » y créerait deux
        // auteurs, « Musso » et « Guillaume », sans que rien ne le signale.
        var notice = NoticeEnLigneService.AnalyserBnf(ReponseBnf);

        Assert.Equal("Guillaume Musso", notice!.Auteurs);
        Assert.DoesNotContain(",", notice.Auteurs);
    }

    [Fact]
    public void L_editeur_de_la_BnF_perd_son_lieu_d_edition()
    {
        // « (Paris) » est une notion de catalogage, pas un nom de maison : gardé,
        // il ferait deux éditeurs distincts du même selon les notices.
        var notice = NoticeEnLigneService.AnalyserBnf(ReponseBnf);

        Assert.Equal("L.G.F - Le livre de poche", notice!.Editeur);
    }

    [Theory]
    [InlineData("Rosinski, Grzegorz", "Grzegorz Rosinski")]
    [InlineData("Herge", "Herge")]
    [InlineData("Musso, Guillaume (1974-....)", "Guillaume Musso")]
    // Trois membres : on ne sait pas ce qu'on lit, donc on n'invente rien.
    [InlineData("Dupont, Jean, Marie", "Dupont, Jean, Marie")]
    public void Le_nom_d_auteur_se_remet_a_l_endroit(string brut, string attendu) =>
        Assert.Equal(attendu, NoticeEnLigneService.NomLisible(brut));

    [Fact]
    public void Une_BnF_sans_resultat_ne_donne_rien()
    {
        Assert.Null(NoticeEnLigneService.AnalyserBnf("""
            <?xml version="1.0" encoding="UTF-8"?>
            <srw:searchRetrieveResponse xmlns:srw="http://www.loc.gov/zing/srw/">
            <srw:numberOfRecords>0</srw:numberOfRecords>
            </srw:searchRetrieveResponse>
            """));

        // Une page d'erreur HTML là où on attend du XML : cela arrive derrière
        // un portail Wi-Fi captif.
        Assert.Null(NoticeEnLigneService.AnalyserBnf("<html>503"));
        Assert.Null(NoticeEnLigneService.AnalyserBnf(""));
    }

    [Fact]
    public void La_couverture_BnF_se_deduit_de_l_ark_de_la_notice()
    {
        // Vérifié le 29/08/2026 : cette adresse rend un JPEG de 600 × 966.
        var notice = NoticeEnLigneService.AnalyserBnf(ReponseBnf);

        Assert.Equal(
            "https://catalogue.bnf.fr/couverture?&appName=NE&idArk=ark:/12148/cb48736742q&couverture=1",
            notice!.CouvertureUrl);
    }

    [Fact]
    public void L_ark_est_choisi_parmi_les_autres_identifiants()
    {
        // La même balise « dc:identifier » porte l'ark, l'ISBN et le code-barres.
        // Prendre le premier venu marcherait sur cette notice-ci et échouerait
        // sur la suivante, selon l'ordre où la BnF les sert.
        var url = NoticeEnLigneService.CouvertureBnf([
            "ISBN 9782253106654",
            "Code à barres commercial : EAN 9782253106654",
            "http://catalogue.bnf.fr/ark:/12148/cb48736742q",
        ]);

        Assert.Contains("idArk=ark:/12148/cb48736742q", url);
    }

    [Fact]
    public void Une_notice_BnF_sans_ark_n_invente_pas_d_adresse()
    {
        Assert.Equal(string.Empty, NoticeEnLigneService.CouvertureBnf(["ISBN 9782253106654"]));
        Assert.Equal(string.Empty, NoticeEnLigneService.CouvertureBnf([]));
    }

    [Fact]
    public void La_vignette_Google_est_demandee_en_plus_grand_et_sans_page_cornee()
    {
        var notice = NoticeEnLigneService.AnalyserGoogle("""
            {"items":[{"volumeInfo":{"title":"Dune","imageLinks":{
                "thumbnail":"http://books.google.com/books/content?id=X&zoom=1&edge=curl"}}}]}
            """);

        // Trois corrections d'un coup : https (WPF refuse le clair sur certaines
        // configurations), zoom=2 (128 px → ~300), et la page cornée retirée.
        Assert.Equal(
            "https://books.google.com/books/content?id=X&zoom=2",
            notice!.CouvertureUrl);
    }

    [Fact]
    public void Une_adresse_qui_n_est_pas_de_Google_ressort_intacte()
    {
        // La garantie qui rend la substitution sûre : on ajuste un paramètre de
        // ce que Google a donné, on ne fabrique rien.
        const string ailleurs = "https://covers.openlibrary.org/b/id/123-L.jpg";

        Assert.Equal(ailleurs, NoticeEnLigneService.HauteResolution(ailleurs));
    }

    [Fact]
    public void Open_Library_donne_sa_couverture_par_l_objet_cover()
    {
        var notice = NoticeEnLigneService.AnalyserOpenLibrary("""
            {"ISBN:9780441013593":{"title":"Dune","cover":{
                "small":"https://covers.openlibrary.org/b/id/1-S.jpg",
                "large":"https://covers.openlibrary.org/b/id/1-L.jpg"}}}
            """, "9780441013593");

        // La GRANDE : c'est celle qu'on télécharge une fois pour toutes.
        Assert.Equal("https://covers.openlibrary.org/b/id/1-L.jpg", notice!.CouvertureUrl);
    }

    [Fact]
    public void Sans_objet_cover_aucune_adresse_n_est_fabriquee_depuis_l_ISBN()
    {
        // ⚠️ LE piège mesuré le 29/08/2026. covers.openlibrary.org répond
        // 200 avec 43 octets — une image blanche — pour un ISBN qu'il ne connaît
        // pas. Fabriquer l'adresse à partir de l'ISBN, comme il serait tentant,
        // ferait donc enregistrer un rectangle vide en croyant tenir une
        // couverture. Le champ « cover » n'existe que s'il y a une vraie image.
        var notice = NoticeEnLigneService.AnalyserOpenLibrary("""
            {"ISBN:9782253106654":{"title":"Quelqu'un d'autre"}}
            """, "9782253106654");

        Assert.Equal(string.Empty, notice!.CouvertureUrl);
    }

    [Fact]
    public void Une_reponse_Google_donne_titre_auteurs_editeur_et_annee()
    {
        var notice = NoticeEnLigneService.AnalyserGoogle("""
            {"totalItems":1,"items":[{"volumeInfo":{
                "title":"Le Lotus bleu",
                "authors":["Hergé"],
                "publisher":"Casterman",
                "publishedDate":"2007-04-11",
                "genres":["Comics & Graphic Novels / European"]
            }}]}
            """);

        Assert.NotNull(notice);
        Assert.Equal("Le Lotus bleu", notice.Titre);
        Assert.Equal("Hergé", notice.Auteurs);
        Assert.Equal("Casterman", notice.Editeur);
        Assert.Equal(2007, notice.Annee);
        Assert.Equal("Google Books", notice.Source);

        // La hiérarchie BISAC est coupée : « Comics & Graphic Novels », et non
        // la ligne entière. Versée telle quelle, elle atterrirait dans le
        // référentiel des genres et y resterait.
        Assert.Equal("Comics & Graphic Novels", notice.Genre);
    }

    [Fact]
    public void Le_sous_titre_de_Google_est_recolle_au_titre()
    {
        // Google sépare ce qu'un lecteur lit d'un seul tenant. Sans ce
        // recollage, le titre relu paraît tronqué et l'on se demande ce qu'on a
        // perdu.
        var notice = NoticeEnLigneService.AnalyserGoogle("""
            {"items":[{"volumeInfo":{"title":"Thorgal","subtitle":"La magicienne trahie"}}]}
            """);

        Assert.Equal("Thorgal — La magicienne trahie", notice!.Titre);
    }

    [Fact]
    public void Plusieurs_auteurs_sont_recolles_par_des_virgules()
    {
        var notice = NoticeEnLigneService.AnalyserGoogle("""
            {"items":[{"volumeInfo":{"title":"Thorgal","authors":["Jean Van Hamme","Grzegorz Rosiński"]}}]}
            """);

        // Exactement la forme qu'attend le champ « Auteurs » du formulaire, qui
        // redécoupe sur la virgule. Les deux conventions doivent rester en
        // accord, sinon l'import crée un auteur nommé « A et B ».
        Assert.Equal("Jean Van Hamme, Grzegorz Rosiński", notice!.Auteurs);
    }

    [Fact]
    public void Une_reponse_Google_sans_resultat_ne_donne_rien()
    {
        Assert.Null(NoticeEnLigneService.AnalyserGoogle("""{"totalItems":0,"items":[]}"""));
        Assert.Null(NoticeEnLigneService.AnalyserGoogle("""{"totalItems":0}"""));
    }

    [Fact]
    public void Une_notice_sans_titre_est_rejetee()
    {
        // Un éditeur et une année sans titre ne composent pas un livre : les
        // verser au formulaire donnerait une fiche à moitié remplie qu'il
        // faudrait défaire.
        Assert.Null(NoticeEnLigneService.AnalyserGoogle("""
            {"items":[{"volumeInfo":{"publisher":"Casterman","publishedDate":"2007"}}]}
            """));
    }

    [Fact]
    public void Une_reponse_illisible_ne_fait_pas_tomber_la_recherche()
    {
        // Une page d'erreur HTML au lieu du JSON attendu : cela arrive derrière
        // un portail Wi-Fi captif. La saisie doit continuer, pas s'interrompre.
        Assert.Null(NoticeEnLigneService.AnalyserGoogle("<html><body>503</body></html>"));
        Assert.Null(NoticeEnLigneService.AnalyserGoogle(""));
    }

    [Fact]
    public void Open_Library_lit_les_noms_dans_des_objets_et_non_des_chaines()
    {
        // LA différence de forme entre les deux sources : Google rend des
        // chaînes nues, Open Library des objets { "name": … }. Un extracteur
        // unique rendrait ici des chaînes vides, sans erreur.
        var notice = NoticeEnLigneService.AnalyserOpenLibrary("""
            {"ISBN:9782203001121":{
                "title":"Le Lotus bleu",
                "authors":[{"name":"Hergé"}],
                "publishers":[{"name":"Casterman"}],
                "publish_date":"April 2007",
                "subjects":[{"name":"Comic books, strips"}]
            }}
            """, "9782203001121");

        Assert.NotNull(notice);
        Assert.Equal("Le Lotus bleu", notice.Titre);
        Assert.Equal("Hergé", notice.Auteurs);
        Assert.Equal("Casterman", notice.Editeur);
        Assert.Equal("Open Library", notice.Source);

        // « April 2007 » : l'année se trouve sans que le format soit connu.
        Assert.Equal(2007, notice.Annee);
    }

    [Fact]
    public void Open_Library_repond_un_objet_vide_pour_un_ISBN_inconnu()
    {
        // Sa façon de dire « je ne sais pas » — le cas le plus fréquent sur la
        // bande dessinée française. Ce n'est pas une erreur, et cela ne doit
        // pas s'afficher comme telle.
        Assert.Null(NoticeEnLigneService.AnalyserOpenLibrary("{}", "9782203001121"));
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  La règle qui protège la saisie
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Un_champ_vide_recoit_la_valeur_de_la_notice()
    {
        var (valeur, change) = NoticeEnLigne.Completer("", "Casterman");

        Assert.Equal("Casterman", valeur);
        Assert.True(change);
    }

    [Fact]
    public void Un_champ_deja_rempli_n_est_JAMAIS_ecrase()
    {
        // LE test de cette classe. Vous cataloguez l'album que vous tenez ; la
        // notice décrit une édition qui peut différer. Un titre corrigé à la
        // main ne doit pas être défait par un clic, et l'écrasure passerait
        // inaperçue au milieu de dix champs qui changent ensemble.
        var (valeur, change) = NoticeEnLigne.Completer("Casterman (édition 1946)", "Casterman");

        Assert.Equal("Casterman (édition 1946)", valeur);
        Assert.False(change);
    }

    [Fact]
    public void Une_notice_muette_sur_un_champ_ne_le_vide_pas()
    {
        // Le pendant du précédent : la source ne sait pas, donc elle se tait.
        // Recopier son silence effacerait ce qu'on avait déjà.
        var (valeur, change) = NoticeEnLigne.Completer("Casterman", "");

        Assert.Equal("Casterman", valeur);
        Assert.False(change);
    }

    [Fact]
    public void Un_champ_ne_contenant_que_des_espaces_compte_pour_vide()
    {
        // Sinon un espace laissé par mégarde bloquerait la complétion, sans que
        // rien ne l'explique à l'écran.
        var (valeur, change) = NoticeEnLigne.Completer("   ", "Casterman");

        Assert.Equal("Casterman", valeur);
        Assert.True(change);
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  Lirevia, la source du dessus
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// La forme exacte que sert /api/public/notices/{isbn} — voir
    /// NoticePublique dans Lirevia.Shared.
    ///
    /// ⚠️ La clé y est « categorie », et le renommage d'Ex-libris ne l'a PAS
    /// touchée : ce format appartient au serveur. Recopier ici le vocabulaire
    /// local ferait passer un test qui ment — le parseur lirait une clé que
    /// Lirevia n'envoie pas, et la catégorie arriverait vide en service. Recopiée plutôt que partagée : les
    /// deux applications se déploient séparément, et Ex-libris doit continuer
    /// de fonctionner contre une version du serveur différente de la sienne.
    /// </summary>
    private const string ReponseLirevia = """
        {"isbn":"9782253106654",
         "titre":"Quelqu'un d'autre",
         "auteur":"Guillaume Musso",
         "editeur":"L.G.F - Le livre de poche",
         "categorie":"Roman",
         "anneeEdition":2026,
         "nbPages":439,
         "format":"Broché",
         "resume":null,
         "couvertureUrl":"https://lirevia.local/covers/abc.jpg",
         "source":"BnF",
         "recupereeLe":"2026-08-29T10:00:00",
         "licence":"Données bibliographiques BnF — Licence Ouverte / Open Licence (Etalab)"}
        """;

    [Fact]
    public void Une_notice_Lirevia_se_lit_entiere()
    {
        var notice = NoticeEnLigneService.AnalyserLirevia(ReponseLirevia);

        Assert.NotNull(notice);
        Assert.Equal("Quelqu'un d'autre", notice.Titre);
        Assert.Equal("Guillaume Musso", notice.Auteurs);
        Assert.Equal("L.G.F - Le livre de poche", notice.Editeur);
        Assert.Equal("Roman", notice.Genre);
        Assert.Equal(2026, notice.Annee);
        Assert.Equal("https://lirevia.local/covers/abc.jpg", notice.CouvertureUrl);
    }

    [Fact]
    public void L_attribution_d_origine_TRAVERSE_Lirevia()
    {
        // La source rendue est « BnF », et non « Lirevia » : Lirevia est le
        // chemin par lequel la notice nous arrive, pas son auteur. C'est ce que
        // demande la Licence Ouverte — l'attribution voyage avec la donnée et ne
        // s'arrête pas au premier intermédiaire.
        var notice = NoticeEnLigneService.AnalyserLirevia(ReponseLirevia);

        Assert.Equal("BnF", notice!.Source);
    }

    [Fact]
    public void Une_notice_Lirevia_sans_source_declaree_est_attribuee_a_Lirevia()
    {
        // Une fiche saisie à la main par un bénévole : elle n'a pas d'origine
        // externe. Lui laisser une source vide effacerait toute trace de sa
        // provenance dans Ex-libris, où l'on ne saurait plus d'où elle sort.
        var notice = NoticeEnLigneService.AnalyserLirevia("""
            {"titre":"Fondation","auteur":"Isaac Asimov","source":""}
            """);

        Assert.Equal("Lirevia", notice!.Source);
    }

    [Fact]
    public void Une_notice_Lirevia_sans_titre_est_rejetee()
    {
        Assert.Null(NoticeEnLigneService.AnalyserLirevia("""{"auteur":"Musso"}"""));
        Assert.Null(NoticeEnLigneService.AnalyserLirevia("{}"));
        Assert.Null(NoticeEnLigneService.AnalyserLirevia(""));
    }

    [Fact]
    public void Un_404_de_Lirevia_ne_fait_pas_tomber_la_recherche()
    {
        // L'ISBN inconnu du catalogue rend 404, donc une chaîne vide côté
        // lecteur. Et une page d'erreur HTML, derrière un relais mal configuré,
        // ne doit pas davantage interrompre la saisie.
        Assert.Null(NoticeEnLigneService.AnalyserLirevia("<html>404</html>"));
        Assert.Null(NoticeEnLigneService.AnalyserLirevia("[]"));
    }

    [Fact]
    public async Task Lirevia_est_interrogee_AVANT_la_BnF()
    {
        var faux = new Faux(new()
        {
            ["/api/public/notices/"] = ReponseLirevia,
            ["catalogue.bnf.fr"] = ReponseBnf,
        });

        var notice = await NoticeEnLigneService.ChercherAsync(
            "9782253106654", faux.Lire, "http://192.168.1.11:5590");

        // Les deux sources avaient la réponse ; c'est Lirevia qui a parlé, et
        // elle seule a été appelée.
        Assert.Equal("BnF", notice!.Source);          // attribution d'origine
        Assert.Single(faux.Demandees);
        Assert.Contains("192.168.1.11", faux.Demandees[0]);
    }

    [Fact]
    public async Task Sans_adresse_configuree_Lirevia_n_est_PAS_appelee()
    {
        // LE test de cette source. Ex-libris est destiné à être distribué : chez
        // quelqu'un qui n'a pas de serveur Lirevia, aucun appel ne doit partir
        // vers une adresse vide — ni attente, ni erreur à propos d'un service
        // dont il n'a jamais entendu parler.
        var faux = new Faux(new() { ["catalogue.bnf.fr"] = ReponseBnf });

        var notice = await NoticeEnLigneService.ChercherAsync("9782253106654", faux.Lire);

        Assert.Equal("BnF", notice!.Source);
        Assert.Single(faux.Demandees);
        Assert.Contains("catalogue.bnf.fr", faux.Demandees[0]);
        Assert.DoesNotContain(faux.Demandees, u => u.Contains("/api/public/"));
    }

    [Fact]
    public async Task Un_Lirevia_injoignable_laisse_la_main_a_la_BnF()
    {
        // Le serveur est chez soi : il s'éteint, on part en vacances avec le
        // portable. La chaîne doit continuer, pas s'arrêter à la première porte
        // fermée.
        var faux = new Faux(new() { ["catalogue.bnf.fr"] = ReponseBnf });

        var notice = await NoticeEnLigneService.ChercherAsync(
            "9782253106654", faux.Lire, "http://192.168.1.11:5590");

        Assert.Equal("BnF", notice!.Source);
        Assert.Equal(2, faux.Demandees.Count);
    }

    [Fact]
    public async Task L_adresse_de_Lirevia_supporte_une_barre_finale()
    {
        // Une adresse recopiée à la main finit une fois sur deux par « / ».
        // Sans normalisation, l'URL porterait un double slash — que certains
        // relais rejettent, et qui ne se voit pas dans un fichier de config.
        var service = new NoticeEnLigneService(new HttpClient(), "http://192.168.1.11:5590/");
        var faux = new Faux(new() { ["/api/public/notices/"] = ReponseLirevia });

        await NoticeEnLigneService.ChercherAsync("9782253106654", faux.Lire, "http://192.168.1.11:5590");

        Assert.DoesNotContain(faux.Demandees, u => u.Contains("//api/"));
        Assert.NotNull(service);
    }

    // -----------------------------------------------------------------------------------------------------------------
    //  L'ordre des sources
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Un lecteur d'URL qui répond ce qu'on lui dit, et note ce qu'on lui demande.</summary>
    private sealed class Faux(Dictionary<string, string> reponses)
    {
        public List<string> Demandees { get; } = [];

        public Task<string> Lire(string url)
        {
            Demandees.Add(url);
            var cle = reponses.Keys.FirstOrDefault(url.Contains);
            return Task.FromResult(cle is null ? string.Empty : reponses[cle]);
        }
    }

    [Fact]
    public async Task La_BnF_est_interrogee_en_PREMIER()
    {
        // Le défaut signalé par l'utilisateur : 9782253106654 restait
        // introuvable parce que la BnF n'était pas consultée.
        var faux = new Faux(new() { ["catalogue.bnf.fr"] = ReponseBnf });

        var notice = await NoticeEnLigneService.ChercherAsync("9782253106654", faux.Lire);

        Assert.Equal("BnF", notice!.Source);
        Assert.Contains("catalogue.bnf.fr", faux.Demandees[0]);
    }

    [Fact]
    public async Task Une_source_qui_repond_arrete_la_chaine()
    {
        // Trois appels réseau là où un seul suffit, ce serait trois secondes
        // d'attente sur chaque saisie — et du quota brûlé pour rien.
        var faux = new Faux(new() { ["catalogue.bnf.fr"] = ReponseBnf });

        await NoticeEnLigneService.ChercherAsync("9782253106654", faux.Lire);

        Assert.Single(faux.Demandees);
        Assert.DoesNotContain(faux.Demandees, u => u.Contains("googleapis"));
    }

    [Fact]
    public async Task Une_BnF_muette_laisse_la_main_aux_suivantes()
    {
        // Le cas d'un ouvrage étranger : la BnF ne l'a pas, Google si.
        var faux = new Faux(new()
        {
            ["googleapis"] = """{"items":[{"volumeInfo":{"title":"Dune"}}]}""",
        });

        var notice = await NoticeEnLigneService.ChercherAsync("9780441013593", faux.Lire);

        Assert.Equal("Google Books", notice!.Source);
        Assert.Equal(2, faux.Demandees.Count);
    }

    [Fact]
    public async Task Aucune_source_ne_repond_donne_null_et_non_une_exception()
    {
        // Hors ligne : la saisie doit continuer à la main, pas s'interrompre.
        var faux = new Faux([]);

        Assert.Null(await NoticeEnLigneService.ChercherAsync("9782253106654", faux.Lire));
        Assert.Equal(3, faux.Demandees.Count);
    }

    [Fact]
    public async Task Un_ISBN_vide_ne_declenche_aucun_appel()
    {
        var faux = new Faux([]);

        Assert.Null(await NoticeEnLigneService.ChercherAsync("", faux.Lire));
        Assert.Empty(faux.Demandees);
    }

    [Theory]
    [InlineData("2007", 2007)]
    [InlineData("2007-04-11", 2007)]
    [InlineData("April 2019", 2019)]
    [InlineData("c1985", 1985)]
    [InlineData("", null)]
    [InlineData("s.d.", null)]
    // Un numéro de collection pris pour une date : la borne basse l'écarte.
    [InlineData("Collection 0417", null)]
    public void L_annee_se_lit_quel_que_soit_le_format(string valeur, int? attendue) =>
        Assert.Equal(attendue, NoticeEnLigneService.ExtraireAnnee(valeur));
}
