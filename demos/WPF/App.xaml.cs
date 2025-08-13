using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PowerSync.Common.Client;
using PowersyncDotnetTodoList.Models;
using PowersyncDotnetTodoList.Services;
using PowersyncDotnetTodoList.ViewModels;
using PowersyncDotnetTodoList.Views;

namespace PowersyncDotnetTodoList
{
    public partial class App : Application
    {
        public static IServiceProvider? Services { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Handle async initialization synchronously or with proper error handling
            try
            {
                InitializeApplicationAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Application startup failed: {ex.Message}\n\nFull error: {ex}",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                // Log to console if available
                Console.WriteLine($"=== APPLICATION STARTUP FAILED ===");
                Console.WriteLine($"Exception: {ex}");

                // Shut down the application
                Current.Shutdown(1);
                return;
            }
        }

        private async Task InitializeApplicationAsync()
        {
            Console.WriteLine("DEBUG: Starting application initialization...");

            var services = new ServiceCollection();
            ConfigureServices(services);
            Console.WriteLine("DEBUG: Services configured");

            // Build the service provider
            Services = services.BuildServiceProvider();
            var mainWindow = Services.GetRequiredService<MainWindow>();
            Console.WriteLine("DEBUG: Got MainWindow");

            Console.WriteLine("DEBUG: Getting NavigationService...");
            var navigationService = Services.GetRequiredService<INavigationService>();
            Console.WriteLine("DEBUG: Got NavigationService");

            Console.WriteLine("DEBUG: Navigating to TodoListViewModel...");
            navigationService.Navigate<TodoListViewModel>();
            Console.WriteLine("DEBUG: Navigation completed");

            Console.WriteLine("DEBUG: Showing main window...");
            mainWindow.Show();
            Console.WriteLine("DEBUG: Main window shown - startup complete!");
        }

        private void ConfigureServices(IServiceCollection services)
        {
            Console.WriteLine("DEBUG: Configuring services...");

            ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Register PowerSyncDatabase
            services.AddSingleton<PowerSyncDatabase>(sp =>
            {
                Console.WriteLine("DEBUG: Creating PowerSyncDatabase...");
                var logger = loggerFactory.CreateLogger("PowerSyncLogger");
                return new PowerSyncDatabase(
                    new PowerSyncDatabaseOptions
                    {
                        Database = new SQLOpenOptions { DbFilename = "example.db" },
                        Schema = AppSchema.PowerSyncSchema,
                        Logger = logger,
                    }
                );
            });

            // Register IPowerSyncDatabase explicitly
            services.AddSingleton<IPowerSyncDatabase>(sp =>
                sp.GetRequiredService<PowerSyncDatabase>()
            );

            // Register PowerSyncConnector
            services.AddSingleton<PowerSyncConnector>(sp =>
            {
                Console.WriteLine("DEBUG: Creating PowerSyncConnector...");
                return new PowerSyncConnector();
            });

            // Register ViewModels and Views
            services.AddTransient<TodoListViewModel>();
            services.AddTransient<TodoViewModel>();
            services.AddTransient<SQLConsoleViewModel>();
            services.AddTransient<TodoListView>();
            services.AddTransient<TodoView>();
            services.AddTransient<SQLConsoleView>();

            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainWindowViewModel>();

            services.AddSingleton<INavigationService>(sp =>
            {
                Console.WriteLine("DEBUG: Creating NavigationService...");
                var mainWindow = sp.GetRequiredService<MainWindow>();
                return new NavigationService(mainWindow.MainFrame, sp);
            });

            Console.WriteLine("DEBUG: Service configuration completed");
        }

        // Add global exception handler
        private void Application_DispatcherUnhandledException(
            object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e
        )
        {
            Console.WriteLine($"=== UNHANDLED EXCEPTION ===");
            Console.WriteLine($"Exception: {e.Exception}");

            MessageBox.Show(
                $"An unhandled exception occurred: {e.Exception.Message}\n\nFull error: {e.Exception}",
                "Unhandled Exception",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            e.Handled = true; // Prevent application crash
        }
    }
}
