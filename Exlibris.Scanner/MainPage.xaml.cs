using System.Collections.ObjectModel;
using BarcodeScanning;
using Exlibris.Scanner.Services;
using Exlibris.Shared.Modeles;

namespace Exlibris.Scanner;

/// <summary>
/// L'écran unique : on saisit un rangement, on passe les livres devant la
/// caméra, on envoie le fichier.
///
/// <b>Un seul écran, et c'est délibéré.</b> On tient un téléphone d'une main et
/// un livre de l'autre, souvent accroupi devant une étagère basse. Chaque
/// navigation supplémentaire est un geste de trop, répété trois cents fois.
///
/// Repris de <c>ScanPage</c> de Lirevia.Mobile, qui avait déjà résolu les
/// questions difficiles : la déduplication des rafales de la caméra, le
/// traitement d'un code à la fois, et le repli en saisie manuelle.
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly InventaireEnCours _inventaire;
    private readonly RechercheTitre _titres;

    private readonly ObservableCollection<string> _journal = [];

    /// <summary>
    /// Vrai pendant qu'un code est en cours de traitement — le temps d'un appel
    /// réseau, deux secondes au plus.
    /// </summary>
    private bool _occupe;

    /// <summary>
    /// Le filtre des rafales.
    ///
    /// <c>_occupe</c> ne suffisait pas, et c'est le défaut constaté sur un
    /// téléphone réel : il ne protège que PENDANT le traitement. Sitôt relâché,
    /// le livre toujours présenté devant l'objectif redéclenchait, et le journal
    /// se remplissait de « déjà relevé » cinq à vingt fois par ouvrage. Le
    /// fichier restait juste ; c'est l'écran qui devenait inutilisable, au
    /// moment précis où l'on compte dessus.
    /// </summary>
    private readonly AntiRafale _antiRafale = new();

    /// <summary>
    /// Le dernier code signalé, pour le scan à la volée : sans lui, un livre
    /// laissé devant l'objectif serait resignalé toutes les trois secondes.
    /// Voir <see cref="DernierCodeLu"/>.
    /// </summary>
    private readonly DernierCodeLu _dernierCode = new();

    private bool ALaVolee => _inventaire.ScanALaVolee;

    /// <summary>
    /// Annule le désarmement automatique quand un livre est lu avant l'échéance.
    /// </summary>
    private CancellationTokenSource? _echeance;

    /// <summary>
    /// Combien de temps la caméra reste à l'écoute après un appui.
    ///
    /// Assez pour reprendre son geste deux ou trois fois sur un code-barres
    /// récalcitrant ; assez court pour qu'un téléphone posé sur le comptoir, le
    /// scan oublié, cesse de lire ce qui passe devant lui.
    /// </summary>
    private static readonly TimeSpan Ecoute = TimeSpan.FromSeconds(20);

    public MainPage(InventaireEnCours inventaire, RechercheTitre titres)
    {
        InitializeComponent();

        _inventaire = inventaire;
        _titres = titres;

        _inventaire.Charger();

        ChampRangement.Text = _inventaire.Rangement;
        ListeScannes.ItemsSource = _journal;

        // Ce qui avait été scanné avant une mise en veille est toujours là :
        // le journal le rappelle, sinon l'écran paraîtrait vide et l'on
        // rescannerait l'étagère entière.
        foreach (var ligne in _inventaire.Lignes.AsEnumerable().Reverse().Take(50))
            _journal.Add($"• {ligne.Isbn}");

        Compter();

        // La mire et la consigne selon le mode retenu, avant la caméra.
        Desarmer();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (await PermissionCameraAsync())
        {
            Camera.CameraEnabled = true;
            Reprendre();
        }
        else
            await DisplayAlertAsync("Caméra",
                "Sans accès à la caméra, utilisez le bouton « Saisir » pour "
                + "entrer les codes à la main.", "Compris");
    }

    protected override void OnDisappearing()
    {
        // Coupée explicitement : laissée active, elle vide la batterie et garde
        // le voyant allumé après le retour à l'écran d'accueil. Un téléphone qui
        // « filme » en permanence inquiète, à juste titre.
        Desarmer();
        Camera.CameraEnabled = false;
        base.OnDisappearing();
    }

    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <b>L'analyse ne tourne que sur demande, et c'est la demande du 04/09/2026.</b>
    ///
    /// En détection continue, le temps d'amener le téléphone au-dessus du livre
    /// puis de l'en retirer suffisait à attraper ce qui traînait sur le chemin —
    /// l'étiquette de prix du libraire, le code du rayon, la couverture
    /// voisine. Le relevé se salissait de codes que personne n'avait visés, et
    /// le journal devenait illisible.
    ///
    /// <c>PauseScanning</c> plutôt que <c>CameraEnabled</c> : couper la caméra
    /// démonte la session, la rallumer coûte une seconde et une remise au point
    /// — l'aperçu resterait noir au moment précis où l'on vise. Ici l'image
    /// continue de vivre, seule l'analyse s'arrête.
    ///
    /// <b>Deux modes depuis le 11/09/2026</b>, au choix dans le menu ⋯ : « au
    /// toucher » (ce qui précède, par défaut) et « à la volée », où l'analyse
    /// tourne tant que l'écran est ouvert. Le second retrouve le risque des codes
    /// attrapés au passage ; c'est un choix d'usage — une pile triée sur une
    /// table n'a pas d'étiquette voisine à craindre — et il se prend sur place.
    ///
    /// <b>Le déclencheur est le CADRE, et non plus un bouton</b> (demande du
    /// 11/09/2026, après essai en magasin). Le bouton, en bas de l'écran,
    /// obligeait à lâcher le livre ou à contorsionner le pouce ; l'index posé sur
    /// le cadre vise et déclenche du même geste. À la volée, le même toucher met
    /// en pause et reprend.
    /// </summary>
    private void ToucherMire(object? envoyeur, TappedEventArgs e)
    {
        if (Camera.PauseScanning) Armer();
        else Desarmer();
    }

    private void Armer()
    {
        // Un livre représenté juste après le précédent doit répondre : sans
        // cela, le silence de trois secondes de l'anti-rafale ferait passer un
        // appui délibéré pour un écho.
        _antiRafale.Reinitialiser();
        _dernierCode.Oublier();

        Camera.PauseScanning = false;

        Mire.Stroke = new SolidColorBrush(Color.FromArgb("#7FD8A0"));
        Mire.BackgroundColor = Color.FromArgb("#227FD8A0");

        if (ALaVolee)
        {
            // Aucune échéance : c'est tout le principe du mode.
            LabelConsigne.Text = "À la volée — touchez le cadre pour une pause";
            return;
        }

        LabelConsigne.Text = "Remplissez le cadre avec le code-barres";
        Compte(Ecoute);
    }

    private void Desarmer()
    {
        _echeance?.Cancel();
        _echeance = null;

        Camera.PauseScanning = true;

        Mire.Stroke = new SolidColorBrush(Color.FromArgb("#4A6C97"));
        Mire.BackgroundColor = Color.FromArgb("#224A6C97");
        LabelConsigne.Text = ALaVolee
            ? "En pause — touchez le cadre pour reprendre"
            : "Touchez le cadre pour scanner un livre";
    }

    /// <summary>
    /// Après une interruption — saisie, menu, envoi, retour à l'écran — : à la
    /// volée on se remet à l'écoute sans qu'on le demande, au toucher on reste
    /// désarmé. Appelé aussi après un changement de mode, pour que la mire et la
    /// consigne suivent.
    /// </summary>
    private void Reprendre()
    {
        if (ALaVolee) Armer();
        else Desarmer();
    }

    /// <summary>
    /// Rend la main à l'écoute passé le délai. Le jeton évite qu'un armement
    /// désarme celui qui l'a suivi : sans lui, deux appuis rapprochés
    /// couperaient le second au bout du délai du premier.
    /// </summary>
    private async void Compte(TimeSpan delai)
    {
        var source = new CancellationTokenSource();
        _echeance = source;

        try
        {
            await Task.Delay(delai, source.Token);

            if (ReferenceEquals(_echeance, source)) Desarmer();
        }
        catch (OperationCanceledException)
        {
            // Un livre est arrivé, ou l'utilisateur a arrêté lui-même.
        }
        finally
        {
            source.Dispose();
        }
    }

    private static async Task<bool> PermissionCameraAsync()
    {
        try
        {
            var etat = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (etat != PermissionStatus.Granted)
                etat = await Permissions.RequestAsync<Permissions.Camera>();

            return etat == PermissionStatus.Granted;
        }
        catch (Exception)
        {
            // Sous Windows, en développement, la permission n'est pas
            // implémentée et lève. Le repli manuel rend l'écran utilisable pour
            // mettre au point le reste sans téléphone.
            return false;
        }
    }

    // -----------------------------------------------------------------------------------------------------------------

    private void RangementChange(object? envoyeur, TextChangedEventArgs e) =>
        _inventaire.Rangement = e.NewTextValue ?? string.Empty;

    private void Compter()
    {
        LabelCompteur.Text = _inventaire.Nombre switch
        {
            0 => "Aucun ouvrage relevé.",
            1 => "1 ouvrage relevé.",
            _ => $"{_inventaire.Nombre} ouvrages relevés.",
        };

        BoutonEnvoyer.IsEnabled = _inventaire.Nombre > 0;
    }

    /// <summary>
    /// ⚠️ On regarde TOUS les résultats de l'image, pas seulement le premier.
    ///
    /// <b>C'est le correctif des non-lectures du 31/08/2026.</b> Une quatrième
    /// de couverture porte souvent plusieurs codes-barres : l'EAN du livre,
    /// l'étiquette de prix du libraire, le code de catégorie de l'éditeur — et,
    /// sur un comptoir, ceux du volume posé dessous. La caméra les remonte
    /// ensemble, dans l'ordre de la détection, qui n'a aucun rapport avec ce
    /// qu'on vise. Ne prendre que le premier revenait à jouer l'ouvrage à pile
    /// ou face : quand l'étiquette sortait devant, l'EAN du livre était rejeté
    /// avec elle, et le livre repartait non lu alors qu'il figurait dans la
    /// même image.
    /// </summary>
    private void CodeDetecte(object? envoyeur, OnDetectionFinishedEventArg e)
    {
        var lus = e.BarcodeResults?.Select(r => r.DisplayValue).ToList();
        if (lus is not { Count: > 0 }) return;

        var livre = ChoixCodeBarres.PremierLivre(lus);

        // La détection arrive sur un fil d'arrière-plan : tout ce qui suit
        // touche l'interface ou part sur le réseau.
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (livre.Length > 0)
            {
                await TraiterAsync(livre);
                return;
            }

            // Aucun livre dans l'image. On le DIT — sinon on insiste sur une
            // étiquette de prix sans comprendre pourquoi rien ne se passe —
            // mais on reste à l'écoute : le geste n'est pas fini, on n'a pas
            // encore trouvé le bon code.
            Refuser(lus[0]);
        });
    }

    /// <summary>
    /// ⚠️ TOUT code-barres n'est pas un livre. Une étagère porte aussi des
    /// étiquettes de prix, des autocollants de bibliothèque, et le carton d'à
    /// côté a le sien.
    ///
    /// Ici on BLOQUE, alors que la saisie dans Ex-libris se contente
    /// d'avertir. La différence tient à qui décide : au clavier, vous savez ce
    /// que vous tapez et pourquoi ; devant une caméra, le code vient de ce qui
    /// passe dans le champ, et l'accepter salirait l'inventaire d'entrées que
    /// personne n'a voulues.
    /// </summary>
    private void Refuser(string? code)
    {
        var lu = code?.Trim();
        if (string.IsNullOrWhiteSpace(lu)) return;

        if (!_antiRafale.Accepte(lu, DateTime.Now)) return;

        // À la volée, l'étiquette de prix restée dans le champ ne se signale
        // qu'une fois. EstLeMeme est évalué EN PREMIER : il doit retenir le code
        // quel que soit le mode.
        if (_dernierCode.EstLeMeme(lu) && ALaVolee) return;

        SignalerRefus(lu);
    }

    /// <summary>
    /// Le refus lui-même, SANS passer par l'anti-rafale — l'appelant filtre, ou a
    /// une raison de ne pas le faire.
    ///
    /// ⚠️ <b>Séparé pour un correctif.</b> Un code tapé à la main qui n'est pas
    /// un livre passait DEUX fois par l'anti-rafale : TraiterAsync l'enregistrait,
    /// puis Refuser le prenait pour son propre écho. Il disparaissait sans un mot
    /// — exactement ce que SaisirALaMain promet d'éviter.
    ///
    /// Pas de vibration à la volée : on ne vise rien, et une secousse pour un code
    /// croisé en chemin se confondrait avec un livre lu.
    /// </summary>
    private void SignalerRefus(string lu)
    {
        if (!ALaVolee) Vibrer(court: false);
        Signaler($"✕ {lu} — pas un code de livre");
    }

    private async Task TraiterAsync(string code)
    {
        // L'écho d'un livre encore devant l'objectif : on sort AVANT tout, sans
        // rien afficher. C'est la seule façon de garder le journal lisible.
        if (_occupe || !_antiRafale.Accepte(code, DateTime.Now)) return;

        if (!Isbn.EstLivre(code))
        {
            SignalerRefus(code.Trim());
            return;
        }

        var isbn = Isbn.Normaliser(code);
        var repetition = _dernierCode.EstLeMeme(isbn);
        string ligne;

        _occupe = true;

        try
        {
            // Le livre est reconnu : on rend la main. Insister devant le même
            // ouvrage n'apporterait plus rien, et l'écoute qui se poursuit est
            // exactement ce qui attrapait des codes que personne ne visait.
            // À la volée, cette écoute est justement ce qu'on a demandé.
            if (!ALaVolee) Desarmer();

            if (!_inventaire.Ajouter(isbn))
            {
                // Le même livre encore devant l'objectif, en continu : il a eu
                // sa ligne trois secondes plus tôt.
                if (ALaVolee && repetition) return;

                // Un livre DÉLIBÉRÉMENT représenté, hors rafale : le dire, sinon
                // on cherche pourquoi le compteur n'a pas bougé et on rescanne.
                Vibrer(court: false);
                Signaler($"↺ {isbn} — déjà relevé");
                return;
            }

            // Le retour haptique compte plus qu'il n'y paraît : on tient le
            // téléphone au-dessus d'un livre, souvent sans regarder l'écran.
            // Sans lui, on attend, on doute, on repasse le livre — et le rythme
            // tombe de moitié sur trois cents ouvrages.
            Vibrer(court: true);
            Compter();

            // La ligne apparaît TOUT DE SUITE avec l'ISBN, puis se complète si
            // le titre arrive. L'attendre avant d'afficher donnerait un scanner
            // qui marque un temps d'arrêt à chaque livre.
            ligne = $"✓ {isbn}";
            _journal.Insert(0, ligne);
        }
        finally
        {
            // ⚠️ RELÂCHÉ AVANT L'APPEL RÉSEAU, et c'est un correctif.
            //
            // La recherche du titre tenait le verrou jusqu'à deux secondes. En
            // librairie, hors du réseau de la maison, chaque appel allait au
            // bout de son délai : le scanner restait sourd deux secondes après
            // CHAQUE ouvrage, et l'on présentait le suivant dans le vide. Le
            // titre n'est qu'un confort ; il n'a jamais eu à barrer la route au
            // livre d'après.
            _occupe = false;
        }

        var titre = await _titres.ChercherAsync(isbn);
        if (titre.Length == 0) return;

        // Retrouvée par son texte, et non par sa position : d'autres lignes ont
        // pu s'insérer au-dessus pendant l'appel, maintenant qu'il ne bloque
        // plus rien.
        var index = _journal.IndexOf(ligne);
        if (index >= 0) _journal[index] = $"✓ {titre}";
    }

    /// <summary>
    /// Une secousse brève pour un ouvrage accepté, deux pour un doublon.
    ///
    /// Enveloppé : la vibration n'est pas disponible partout — Windows en
    /// développement, ou un téléphone dont l'utilisateur l'a coupée — et une
    /// exception ici arrêterait un scan pour un détail de confort.
    /// </summary>
    private static void Vibrer(bool court)
    {
        try
        {
            if (court) HapticFeedback.Perform(HapticFeedbackType.Click);
            else HapticFeedback.Perform(HapticFeedbackType.LongPress);
        }
        catch (Exception)
        {
            // Sans retour haptique, l'application reste parfaitement utilisable.
        }
    }

    private void Signaler(string ligne)
    {
        _journal.Insert(0, ligne);
        while (_journal.Count > 60) _journal.RemoveAt(_journal.Count - 1);
    }

    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Saisie manuelle — un repli qui sert tous les jours, pas une politesse.
    /// Une étiquette gondolée, un film plastique qui reflète, un code-barres
    /// arraché : cela arrive plusieurs fois par étagère.
    /// </summary>
    private async void SaisirALaMain(object? envoyeur, EventArgs e)
    {
        Desarmer();

        var saisie = await DisplayPromptAsync(
            "Saisie manuelle", "Code inscrit sous les barres :",
            accept: "Ajouter", cancel: "Annuler", keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(saisie))
        {
            Reprendre();
            return;
        }

        // Une saisie au clavier doit TOUJOURS obtenir une réponse, fût-elle un
        // refus. Sans cet oubli, le code qu'on vient de taper parce que la
        // caméra ne le lisait pas serait pris pour l'écho de cette lecture, et
        // disparaîtrait en silence — au moment précis où l'on cherche pourquoi
        // rien ne se passe.
        _antiRafale.Reinitialiser();
        _dernierCode.Oublier();

        await TraiterAsync(saisie.Trim());
        Reprendre();
    }

    /// <summary>
    /// Écrit le CSV et ouvre le partage du système — courriel, cloud, câble :
    /// c'est à l'utilisateur de choisir par où il passe.
    ///
    /// <b>Le relevé n'est vidé qu'APRÈS confirmation</b>, et seulement si
    /// l'utilisateur le demande. Un partage peut n'aboutir nulle part : effacer
    /// d'office ferait perdre une heure de travail pour un courriel jamais
    /// parti.
    /// </summary>
    private async void Envoyer(object? envoyeur, EventArgs e)
    {
        if (_inventaire.Nombre == 0) return;

        Desarmer();

        try
        {
            var chemin = _inventaire.EcrireFichier();

            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "Inventaire Ex-libris",
                File = new ShareFile(chemin),
            });

            var vider = await DisplayAlertAsync(
                "Inventaire envoyé ?",
                $"{_inventaire.Nombre} ouvrages ont été écrits dans le fichier.\n\n"
                + "Vider le relevé pour commencer une nouvelle série ? "
                + "Ne le faites qu'une fois le fichier bien arrivé sur l'ordinateur.",
                "Vider", "Garder");

            if (vider)
            {
                _inventaire.Vider();
                _journal.Clear();
                Compter();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Envoi impossible", ex.Message, "Fermer");
        }
        finally
        {
            Reprendre();
        }
    }

    private void BasculerLampe(object? envoyeur, EventArgs e)
    {
        Camera.TorchOn = !Camera.TorchOn;
        BoutonLampe.TextColor = Camera.TorchOn
            ? Color.FromArgb("#F0C674")
            : Color.FromArgb("#8FB0D8");
    }

    private async void Menu(object? envoyeur, EventArgs e)
    {
        Desarmer();

        // Le mode en cours se lit dans l'intitulé : on sait où l'on en est sans
        // ouvrir le second menu.
        var optionMode = $"Mode de scan : {(ALaVolee ? "à la volée" : "au toucher")}";

        var choix = await DisplayActionSheetAsync("Inventaire", "Annuler", null,
            optionMode, "Vider le relevé", "Adresse de Lirevia", "Tester la connexion");

        if (choix == optionMode) await ChoisirModeAsync();

        switch (choix)
        {
            case "Vider le relevé": await ViderAsync(); break;
            case "Adresse de Lirevia": await AdresseAsync(); break;
            case "Tester la connexion": await TesterAsync(); break;
        }

        Reprendre();
    }

    private async Task ChoisirModeAsync()
    {
        const string AuToucher = "Au toucher — un livre par toucher du cadre";
        const string EnContinu = "À la volée — en continu";

        var choix = await DisplayActionSheetAsync("Mode de scan", "Annuler", null, AuToucher, EnContinu);

        if (choix == AuToucher) _inventaire.ScanALaVolee = false;
        else if (choix == EnContinu) _inventaire.ScanALaVolee = true;
    }

    /// <summary>
    /// Efface le relevé. <b>Avec confirmation, et en annonçant le nombre.</b>
    ///
    /// C'est la seule action destructrice de l'application : une heure de
    /// travail disparaît, sans corbeille ni retour. Annoncer « 187 ouvrages »
    /// plutôt que « le relevé » laisse une chance de se raviser à celui qui
    /// croyait avoir déjà envoyé son fichier.
    /// </summary>
    private async Task ViderAsync()
    {
        if (_inventaire.Nombre == 0)
        {
            await DisplayAlertAsync("Relevé vide", "Il n'y a rien à effacer.", "Fermer");
            return;
        }

        var confirme = await DisplayAlertAsync(
            "Vider le relevé ?",
            $"{_inventaire.Nombre} ouvrages seront effacés définitivement. "
            + "Si le fichier n'est pas encore arrivé sur l'ordinateur, envoyez-le d'abord.",
            "Effacer", "Annuler");

        if (!confirme) return;

        _inventaire.Vider();
        _journal.Clear();
        Compter();
    }

    private async Task AdresseAsync()
    {
        var saisie = await DisplayPromptAsync(
            "Adresse de Lirevia",
            "Pour afficher les titres pendant le scan. Laissez vide pour vous en passer.",
            accept: "Enregistrer", cancel: "Annuler",
            placeholder: "http://192.168.1.11:5002",
            initialValue: _inventaire.AdresseLirevia);

        if (saisie is not null) _inventaire.AdresseLirevia = saisie.Trim();
    }

    /// <summary>
    /// Interroge Lirevia et DIT ce qui s'est passé.
    ///
    /// <b>Ajouté parce que l'échec était muet.</b> La recherche de titre avale
    /// délibérément toutes ses erreurs — elle ne doit jamais interrompre un scan
    /// — mais ce silence rendait indiscernables une adresse fautive, un serveur
    /// éteint et un blocage d'Android. Sans retour, « cela ne semble rien
    /// faire » est tout ce qu'on peut constater.
    /// </summary>
    private async Task TesterAsync()
    {
        if (_inventaire.AdresseLirevia.Length == 0)
        {
            await DisplayAlertAsync("Aucune adresse",
                "Renseignez d'abord l'adresse de Lirevia.", "Fermer");
            return;
        }

        var (ok, detail) = await _titres.DiagnostiquerAsync("9782253106654");

        await DisplayAlertAsync(ok ? "Connexion établie" : "Connexion impossible",
            detail, "Fermer");
    }
}
