using System.Windows;

namespace Exlibris;

/// <summary>
/// La coquille. Aucun code : le DataContext est posé par App.xaml.cs, et tout
/// le comportement vit dans MainViewModel.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
