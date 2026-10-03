using System.Text.Json;
using Exlibris.Shared.Modeles;

namespace Exlibris.Scanner.Services;

/// <summary>
/// Ce qui a été scanné, et qui doit survivre à tout.
///
/// <b>La persistance n'est pas un confort, c'est la condition de l'usage.</b>
/// Inventorier trois cents ouvrages prend une heure ; pendant cette heure le
/// téléphone se met en veille, on répond à un appel, Android récupère la
/// mémoire d'une application au second plan. Une liste gardée seulement en
/// mémoire disparaîtrait à ce moment-là — et l'on ne s'en apercevrait qu'en
/// revenant, devant une étagère à moitié faite dont on ne sait plus où elle
/// s'est arrêtée.
///
/// Chaque scan est donc écrit immédiatement. Le fichier est minuscule : trois
/// cents lignes font une vingtaine de kilo-octets, et l'écriture est
/// imperceptible entre deux livres.
/// </summary>
public class InventaireEnCours
{
    private readonly string _chemin = Path.Combine(
        FileSystem.AppDataDirectory, "inventaire-en-cours.json");

    private List<LigneInventaire> _lignes = [];

    /// <summary>Les ISBN déjà relevés, pour ne pas compter deux fois le même livre.</summary>
    private HashSet<string> _vus = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<LigneInventaire> Lignes => _lignes;
    public int Nombre => _lignes.Count;

    /// <summary>
    /// Le rangement courant, appliqué aux scans suivants.
    ///
    /// Retenu lui aussi : on passe au meuble d'à côté, le téléphone s'éteint,
    /// et le rangement qu'on venait de saisir serait perdu — les vingt livres
    /// suivants partiraient alors sous l'emplacement précédent, sans que rien
    /// ne le signale.
    /// </summary>
    public string Rangement
    {
        get => Preferences.Get("rangement", string.Empty);
        set => Preferences.Set("rangement", value ?? string.Empty);
    }

    /// <summary>
    /// L'adresse de l'API Lirevia, pour afficher le titre pendant le scan.
    /// Vide : on se contente de l'ISBN, et rien n'est demandé au réseau.
    /// </summary>
    public string AdresseLirevia
    {
        get => Preferences.Get("lirevia", string.Empty);
        set => Preferences.Set("lirevia", (value ?? string.Empty).TrimEnd('/'));
    }

    /// <summary>
    /// Scan à la volée (<c>true</c>) ou au toucher du cadre (<c>false</c>, le
    /// défaut). Retenu : le mode tient à la façon de travailler — une pile triée
    /// sur une table, ou un rayon de librairie plein d'étiquettes — et n'a pas à
    /// être redemandé à chaque ouverture.
    /// </summary>
    public bool ScanALaVolee
    {
        get => Preferences.Get("scan_volee", false);
        set => Preferences.Set("scan_volee", value);
    }

    public void Charger()
    {
        try
        {
            if (!File.Exists(_chemin)) return;

            _lignes = JsonSerializer.Deserialize<List<LigneInventaire>>(
                File.ReadAllText(_chemin)) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            // Un fichier illisible ne doit pas empêcher de démarrer : on repart
            // d'une liste vide plutôt que de refuser de s'ouvrir. Le fichier
            // sera écrasé au premier scan.
            _lignes = [];
        }

        _vus = _lignes.Select(l => l.Isbn).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ajoute un ISBN au relevé. Renvoie <c>false</c> s'il y était déjà.
    ///
    /// Le refus du doublon se fait ICI et non à l'import, parce que c'est ici
    /// qu'on peut le DIRE : « déjà scanné » devant l'étagère évite de chercher
    /// pourquoi le compteur n'a pas bougé.
    /// </summary>
    public bool Ajouter(string isbn)
    {
        var propre = Isbn.Normaliser(isbn);
        if (propre.Length == 0 || !_vus.Add(propre)) return false;

        _lignes.Add(new LigneInventaire { Isbn = propre, Rangement = Rangement });
        Enregistrer();
        return true;
    }

    /// <summary>
    /// Vide le relevé. Appelé APRÈS un partage réussi, jamais avant : le
    /// fichier peut ne jamais arriver à destination, et l'utilisateur doit
    /// pouvoir repartager sans avoir à tout rescanner.
    /// </summary>
    public void Vider()
    {
        _lignes.Clear();
        _vus.Clear();
        Enregistrer();
    }

    /// <summary>
    /// Écrit le CSV dans un fichier temporaire et rend son chemin, prêt à être
    /// partagé.
    ///
    /// <b>UTF-8 AVEC signature.</b> Sans elle, Excel lit le fichier en ANSI et
    /// affiche « étagère » : le fichier est pourtant juste, mais celui qui
    /// l'ouvre conclut que le scanner est cassé.
    /// </summary>
    public string EcrireFichier()
    {
        var nom = $"inventaire-{DateTime.Now:yyyy-MM-dd-HHmm}.csv";
        var chemin = Path.Combine(FileSystem.CacheDirectory, nom);

        File.WriteAllText(chemin, LigneInventaire.Composer(_lignes),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        return chemin;
    }

    private void Enregistrer()
    {
        try
        {
            File.WriteAllText(_chemin, JsonSerializer.Serialize(_lignes));
        }
        catch (IOException)
        {
            // Disque plein, ou fichier verrouillé une fraction de seconde. Le
            // scan en cours reste en mémoire ; le prochain réessaiera.
        }
    }
}
