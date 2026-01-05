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

                // Shut down the application
                Current.Shutdown(1);
                return;
            }
        }

        private async Task InitializeApplicationAsync()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);

            // Build the service provider
            Services = services.BuildServiceProvider();
            var mainWindow = Services.GetRequiredService<MainWindow>();
            var navigationService = Services.GetRequiredService<INavigationService>();

            navigationService.Navigate<TodoListViewModel>();
            mainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Register PowerSyncDatabase
            services.AddSingleton<PowerSyncDatabase>(sp =>
            {
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
                var mainWindow = sp.GetRequiredService<MainWindow>();
                return new NavigationService(mainWindow.MainFrame, sp);
            });
        }

        // Add global exception handler
        private void Application_DispatcherUnhandledException(
            object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e
        )
        {
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
