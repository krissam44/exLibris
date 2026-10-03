using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace Exlibris.Photino.Composants;

/// <summary>
/// Base des formulaires affichés dans un dialogue. Pendant de
/// <see cref="VueBase{TViewModel}"/>, mais le ViewModel est REÇU (il est
/// construit par l'écran appelant, propre à une saisie) au lieu d'être injecté.
///
/// Relaie lui aussi <see cref="INotifyPropertyChanged"/> : la recherche par ISBN
/// remplit les champs après un appel réseau, et « Ma catégorie » se remplit
/// quand on choisit le genre — sans ce relais, l'écran ne le montrerait pas.
/// </summary>
public abstract class FormulaireBase<TViewModel> : ComponentBase, IDisposable
    where TViewModel : class, INotifyPropertyChanged
{
    [Parameter, EditorRequired] public TViewModel Vm { get; set; } = default!;

    private TViewModel? _abonne;

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_abonne, Vm)) return;

        if (_abonne is not null) _abonne.PropertyChanged -= SurChangement;
        Vm.PropertyChanged += SurChangement;
        _abonne = Vm;
    }

    private void SurChangement(object? sender, PropertyChangedEventArgs e) =>
        _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        if (_abonne is not null) _abonne.PropertyChanged -= SurChangement;
    }
}
