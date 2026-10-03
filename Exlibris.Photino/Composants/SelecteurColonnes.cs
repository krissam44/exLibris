namespace Exlibris.Photino.Composants;

/// <summary>Une colonne de la grille, telle que la voit la liste à cocher.</summary>
public sealed class ColonneVisible(string cle, string libelle)
{
    /// <summary>Le nom du champ affiché — le même que le MappingName du WPF.</summary>
    public string Cle { get; } = cle;
    public string Libelle { get; } = libelle;
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Empêche de décocher la DERNIÈRE colonne restante. Une grille sans aucune
    /// colonne n'affiche rien et ne laisse aucun moyen visible de revenir en
    /// arrière : l'utilisateur croit avoir perdu ses livres.
    /// </summary>
    public bool Verrouille { get; set; }
}

/// <summary>
/// Pendant Blazor du SelecteurColonnes WPF : quelles colonnes sont montrées,
/// et le souvenir de ce choix d'une session à l'autre.
///
/// <b>Même réglage que le WPF</b>, au caractère près : les colonnes MASQUÉES,
/// séparées par « | », sous la même clé. Les deux interfaces partagent la
/// base, donc ce choix — décocher « Rangement » ici le décoche là-bas.
///
/// Ce qu'on mémorise, ce sont les colonnes masquées et jamais les visibles :
/// une colonne ajoutée dans une version ultérieure apparaîtra chez les
/// utilisateurs existants, au lieu d'être cachée à tous sauf aux nouvelles
/// installations.
/// </summary>
public sealed class SelecteurColonnes(IEnumerable<ColonneVisible> colonnes, Func<string> lire, Action<string> ecrire)
{
    private const char Separateur = '|';

    /// <summary>Toutes les colonnes de la grille, dans l'ordre d'affichage.</summary>
    private readonly List<ColonneVisible> _toutes = colonnes.ToList();

    private HashSet<string> _imposeesMasquees = [];

    /// <summary>Celles que la liste à cocher propose.</summary>
    public IEnumerable<ColonneVisible> Proposees => _toutes.Where(c => !_imposeesMasquees.Contains(c.Cle));

    /// <summary>
    /// (Re)construit l'état : d'abord les colonnes que l'application impose,
    /// puis le choix mémorisé. <paramref name="imposeesMasquees"/> porte les
    /// colonnes qu'un réglage retire du jeu — « Propriétaire » quand la notion
    /// est désactivée. Elles sont masquées ET absentes de la liste : proposer
    /// de montrer une colonne toujours vide serait une invitation à un ennui.
    /// </summary>
    public void Construire(ISet<string> imposeesMasquees)
    {
        _imposeesMasquees = [.. imposeesMasquees];
        var memorisees = lire().Split(Separateur, StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        foreach (var colonne in _toutes)
            colonne.Visible = !_imposeesMasquees.Contains(colonne.Cle) && !memorisees.Contains(colonne.Cle);

        Verrouiller();
    }

    public bool EstVisible(string cle) => _toutes.FirstOrDefault(c => c.Cle == cle)?.Visible ?? true;

    public void Basculer(ColonneVisible colonne, bool visible)
    {
        if (colonne.Verrouille || colonne.Visible == visible) return;

        colonne.Visible = visible;
        ecrire(string.Join(Separateur, Proposees.Where(c => !c.Visible).Select(c => c.Cle)));
        Verrouiller();
    }

    private void Verrouiller()
    {
        var visibles = Proposees.Count(c => c.Visible);
        foreach (var colonne in Proposees)
            colonne.Verrouille = visibles == 1 && colonne.Visible;
    }
}
