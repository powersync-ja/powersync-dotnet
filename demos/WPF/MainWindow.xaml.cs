using PowerSync.Common.Client;
using PowersyncDotnetTodoList.Services;
using PowersyncDotnetTodoList.ViewModels;

namespace PowersyncDotnetTodoList;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly PowerSyncConnector _connector;
    private readonly PowerSyncDatabase _db; // Replace with your actual database interface/type

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
            Console.WriteLine("DEBUG: Initializing database...");
            await _db.Init();
            Console.WriteLine("DEBUG: Database initialized");

            Console.WriteLine("DEBUG: Connecting to database...");
            await _db.Connect(_connector);
            Console.WriteLine("DEBUG: Database connected");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed to initialize or connect to database: {ex.Message}");
            // Optionally show an error message to the user
            MessageBox.Show(
                $"Failed to initialize database: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }
}
