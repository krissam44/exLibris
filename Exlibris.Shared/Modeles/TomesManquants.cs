namespace Exlibris.Shared.Modeles;

/// <summary>
/// Ce qu'il manque dans une série — la seule chose qu'Ex-libris sache faire et
/// qu'aucune autre application de la maison ne fait.
///
/// <b>Une fonction pure, volontairement.</b> Le calcul aurait pu s'exprimer en
/// SQL, ou vivre dans le ViewModel. Il est ici parce que c'est la règle métier
/// qu'on relira dans deux ans devant une étagère, et qu'une règle qu'on peut
/// éprouver sans base ni fenêtre est une règle qu'on éprouve vraiment.
/// </summary>
public static class TomesManquants
{
    /// <summary>
    /// Les numéros absents, dans l'ordre croissant.
    ///
    /// ⚠️ TROIS DÉCISIONS, et ce sont elles qui font la valeur du résultat.
    ///
    /// 1. <b>La plage commence à 1, jamais au plus petit tome possédé.</b>
    ///    Qui possède les tomes 5 à 7 de Thorgal ne « possède pas une série
    ///    complète de trois volumes » : il lui manque les quatre premiers, et
    ///    c'est précisément ce qu'on est venu chercher. Partir du minimum
    ///    possédé aurait déclaré cette série complète — le silence le plus
    ///    coûteux de tout l'écran.
    ///
    /// 2. <b>La plage s'arrête au plus grand des deux</b> : le plus haut tome
    ///    possédé, ou le nombre annoncé. Sans le nombre annoncé on ne voit que
    ///    les trous INTÉRIEURS (1, 2, 3, 5 → il manque le 4) ; avec lui, on voit
    ///    la queue (1 à 20 sur 24), qui est la vraie liste de courses. Prendre
    ///    le plus grand des deux protège du cas où l'on possède un tome au-delà
    ///    de l'annonce — une intégrale numérotée, un nombre saisi trop bas —
    ///    qui produirait sinon une plage négative.
    ///
    /// 3. <b>Aucun tome numéroté possédé ⇒ aucun manquant.</b> Une série sans
    ///    numérotation est une intégrale, un recueil, un hors-série : rien n'y
    ///    autorise à raisonner sur des rangs. La règle 1 y répondrait
    ///    « il vous manque les tomes 1 à 24 » à quelqu'un qui possède l'œuvre
    ///    entière en un volume.
    /// </summary>
    public static IReadOnlyList<int> Calculer(IEnumerable<int> possedes, int? tomesAnnonces)
    {
        // Les rangs nuls ou négatifs n'existent pas : un « tome 0 » est un
        // hors-série, et la base le laisse à NULL. Écarté ici aussi, au cas où
        // une saisie à la main en aurait posé un.
        var tenus = possedes.Where(t => t > 0).ToHashSet();
        if (tenus.Count == 0) return [];

        var borne = Math.Max(tenus.Max(), tomesAnnonces ?? 0);

        var manquants = new List<int>();
        for (var tome = 1; tome <= borne; tome++)
            if (!tenus.Contains(tome))
                manquants.Add(tome);

        return manquants;
    }

    /// <summary>
    /// Les manquants sous forme lisible : « 4, 7 à 9, 21 à 24 ».
    ///
    /// Le regroupement en plages n'est pas de la coquetterie. Une série de
    /// quarante volumes dont on possède le premier rendrait sinon une cellule
    /// de trente-neuf nombres séparés par des virgules, illisible dans une
    /// grille et inutilisable devant un rayon de librairie.
    /// </summary>
    public static string Resumer(IEnumerable<int> manquants)
    {
        var liste = manquants.ToList();
        if (liste.Count == 0) return string.Empty;

        var morceaux = new List<string>();

        var debut = liste[0];
        var precedent = debut;

        foreach (var tome in liste.Skip(1))
        {
            if (tome == precedent + 1) { precedent = tome; continue; }

            morceaux.Add(Plage(debut, precedent));
            debut = precedent = tome;
        }

        morceaux.Add(Plage(debut, precedent));
        return string.Join(", ", morceaux);
    }

    // « 8 » pour un tome isolé, « 8 et 9 » pour deux : « 8 à 9 » se lit comme
    // une plage longue et fait chercher un troisième numéro qui n'existe pas.
    private static string Plage(int debut, int fin) =>
        debut == fin      ? debut.ToString()
        : fin == debut + 1 ? $"{debut} et {fin}"
        : $"{debut} à {fin}";
}
