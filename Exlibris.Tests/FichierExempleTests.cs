using Exlibris.Data;
using Exlibris.Shared.Modeles;

namespace Exlibris.Tests;

/// <summary>
/// Le fichier d'exemple livre avec le projet doit rester importable.
///
/// Il ne sert pas qu'à la démonstration : c'est le seul endroit où le format
/// est éprouvé tel qu'un éditeur de texte l'écrit vraiment — avec sa signature
/// UTF-8, ses fins de ligne Windows et ses lignes bancales. Les autres tests
/// composent leur contenu en mémoire, ce qui masque toute question d'encodage.
/// </summary>
public class FichierExempleTests : IDisposable
{
    private readonly BancEssai _banc = new();

    public void Dispose() => _banc.Dispose();

    private static string Chemin()
    {
        var dossier = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dossier is not null; i++)
        {
            var candidat = Path.Combine(dossier, "docs", "inventaire-exemple.csv");
            if (File.Exists(candidat)) return candidat;
            dossier = Path.GetDirectoryName(dossier);
        }
        throw new FileNotFoundException("docs/inventaire-exemple.csv introuvable");
    }

    [Fact]
    public void Le_fichier_d_exemple_s_importe_et_donne_ce_qu_il_annonce()
    {
        var lignes = LigneInventaire.Analyser(File.ReadAllText(Chemin()));

        // Onze lignes brutes, dont une vide et une sans ISBN : neuf exploitables.
        Assert.Equal(9, lignes.Count);

        // Le rangement au point-virgule a survécu.
        Assert.Contains(lignes, l => l.Rangement == "Grenier; carton du fond");

        // L'ISBN à tirets est normalisé.
        Assert.Contains(lignes, l => l.Isbn == "9782070360024");

        // Les accents ont traversé la signature UTF-8.
        Assert.Contains(lignes, l => l.Rangement.Contains("étagère"));

        // Et l'import n'en crée que huit : le doublon interne est absorbé.
        var bilan = new InventaireDepot(_banc.Fabrique).Importer(lignes);

        Assert.Equal(8, bilan.Crees);
        Assert.Equal(1, bilan.DejaPresents);
        Assert.Equal(9, bilan.Total);
    }
}
