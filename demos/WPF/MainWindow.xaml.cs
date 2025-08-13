using PowerSync.Common.Client;
using PowersyncDotnetTodoList.Services;
using PowersyncDotnetTodoList.ViewModels;

namespace PowersyncDotnetTodoList;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly PowerSyncConnector _connector;
    private readonly PowerSyncDatabase _db;

    public MainWindow(
        MainWindowViewModel viewModel,
        PowerSyncConnector connector,
        PowerSyncDatabase db
    )
    {
        InitializeComponent();

        _viewModel = viewModel;
        _connector = connector;
        _db = db;

        this.DataContext = _viewModel;

        // Start the async initialization
        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        try
        {
            await _db.Init();
            await _db.Connect(_connector);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to initialize database: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }
}
