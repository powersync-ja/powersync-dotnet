using System.Windows;
using PowersyncDotnetTodoList.ViewModels;

namespace PowersyncDotnetTodoList;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();

        this.DataContext = viewModel;
    }
}
