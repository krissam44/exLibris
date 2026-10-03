using Dapper;

namespace Exlibris.Data;

/// <summary>
/// Les réglages de l'application : une clé, une valeur.
///
/// <b>Un couple plutôt qu'une colonne par réglage.</b> Une table
/// <c>parametres(actifs BOOLEAN, theme TEXT, …)</c> exigerait une migration à
/// chaque nouveau réglage, sur toutes les bases déjà installées — pour un
/// besoin qui, ici, tient en une case à cocher. Le couple clé/valeur ne coûte
/// que l'interprétation du texte, faite une fois ci-dessous.
///
/// Le prix de ce choix est réel et il faut le nommer : rien ne garantit qu'une
/// clé écrite quelque part soit celle qu'on relit ailleurs. D'où les constantes
/// <see cref="ProprietairesActifs"/> — une faute de frappe devient alors une
/// erreur de compilation, et non un réglage qui ne prend jamais.
/// </summary>
public class ParametreDepot(FabriqueConnexion fabrique)
{
    /// <summary>
    /// Affiche-t-on le propriétaire de chaque ouvrage ?
    ///
    /// Faux par défaut, et c'est le bon défaut : la plupart des bibliothèques
    /// n'ont qu'un propriétaire, et leur montrer une case dont la réponse est
    /// toujours la même n'apporte rien. Ceux qui en ont besoin l'activent une
    /// fois.
    /// </summary>
    public const string ProprietairesActifs = "proprietaires.actifs";

    /// <summary>
    /// La valeur brute, ou <paramref name="defaut"/> si le réglage n'a jamais
    /// été écrit. Ne rend jamais <c>null</c> : un appelant qui oublierait de
    /// tester le cas obtiendrait sinon une exception au premier démarrage, seul
    /// moment où le réglage manque vraiment.
    /// </summary>
    public string Lire(string cle, string defaut = "")
    {
        using var con = fabrique.Ouvrir();

        return con.ExecuteScalar<string?>(
            "SELECT valeur FROM parametres WHERE cle = @cle", new { cle }) ?? defaut;
    }

    public bool LireBooleen(string cle, bool defaut = false)
    {
        // Une valeur absente ou incompréhensible rend le DÉFAUT, jamais false :
        // le jour où un réglage aura « true » pour défaut, une base ancienne ne
        // doit pas le voir désactivé sans que personne l'ait demandé.
        return bool.TryParse(Lire(cle), out var resultat) ? resultat : defaut;
    }

    public void EcrireBooleen(string cle, bool valeur) =>
        Ecrire(cle, valeur ? "true" : "false");

    public void Ecrire(string cle, string valeur)
    {
        using var con = fabrique.Ouvrir();

        // UPSERT : le réglage peut n'avoir jamais été écrit, ou l'avoir été dix
        // fois. Un INSERT précédé d'un SELECT ferait deux allers-retours pour
        // dire la même chose.
        con.Execute("""
            INSERT INTO parametres (cle, valeur) VALUES (@cle, @valeur)
            ON CONFLICT(cle) DO UPDATE SET valeur = excluded.valeur
            """, new { cle, valeur });
    }
}
