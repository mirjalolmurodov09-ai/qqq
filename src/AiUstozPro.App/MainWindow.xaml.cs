using System.Windows;
using AiUstozPro.App.ViewModels;

namespace AiUstozPro.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
