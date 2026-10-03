using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace Exlibris.Photino.Composants;

/// <summary>
/// Base des pages qui affichent un ViewModel du projet WPF.
///
/// Le XAML se redessine tout seul quand une propriété notifie son changement ;
/// Blazor, lui, ne redessine qu'à la fin d'un gestionnaire d'événement. Une
/// valeur modifiée ailleurs — une recherche en ligne qui aboutit, un traitement
/// par lot qui avance — resterait donc invisible. Cette base relaie
/// <see cref="INotifyPropertyChanged"/> vers <c>StateHasChanged</c>, ce qui
/// permet de reprendre les ViewModels sans les retoucher.
/// </summary>
public abstract class VueBase<TViewModel> : ComponentBase, IDisposable
    where TViewModel : INotifyPropertyChanged
{
    [Inject] protected TViewModel Vm { get; set; } = default!;

    protected override void OnInitialized()
    {
        Vm.PropertyChanged += SurChangement;
        // Le rafraîchissement À L'ARRIVÉE, comme MainViewModel.Rafraichir dans
        // le WPF : les ViewModels étant conservés d'une visite à l'autre, la
        // page afficherait sinon l'état de la visite précédente.
        Arrivee();
    }

    /// <summary>Appelé à chaque arrivée sur la page.</summary>
    protected virtual void Arrivee() { }

    /// <summary>
    /// Appelé quand une propriété du ViewModel change, AVANT le redessin.
    /// Pour les pages qui tiennent une copie de l'état (une grille qui ne doit
    /// pas suivre une collection élément par élément, par exemple).
    /// </summary>
    protected virtual void ProprieteChangee(string? nom) { }

    // InvokeAsync : la notification peut venir d'une tâche de fond (await sans
    // contexte), hors du fil de rendu de Blazor.
    private void SurChangement(object? sender, PropertyChangedEventArgs e) =>
        _ = InvokeAsync(() =>
        {
            ProprieteChangee(e.PropertyName);
            StateHasChanged();
        });

    public virtual void Dispose() => Vm.PropertyChanged -= SurChangement;
}
