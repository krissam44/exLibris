using System.Windows.Controls;

namespace Exlibris.Vues;

/// <summary>
/// Le corps du dialogue de saisie. Aucun code : il est monté dans
/// <c>DialogueBase</c> par <c>DialogueService</c>, et son DataContext est un
/// <c>LivreFormulaireViewModel</c>.
/// </summary>
public partial class LivreFormulaireVue : UserControl
{
    public LivreFormulaireVue() => InitializeComponent();
}
