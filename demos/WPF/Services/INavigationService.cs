using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using PowersyncDotnetTodoList.Models;
using PowersyncDotnetTodoList.ViewModels;
using PowersyncDotnetTodoList.Views;

namespace PowersyncDotnetTodoList.Services
{
    public interface INavigationService
    {
        void Navigate<T>()
            where T : class;
        void Navigate<T>(object parameter)
            where T : class;
        void GoBack();
    }
}

namespace PowersyncDotnetTodoList.Services
{
    public class NavigationService : INavigationService
    {
        private readonly Frame _frame;
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<Type, Type> _viewModelToViewMappings = [];

        public NavigationService(Frame frame, IServiceProvider serviceProvider)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
            _serviceProvider =
                serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            // Register your view-viewmodel mappings here
            RegisterMapping<MainWindowViewModel, MainWindow>();
            RegisterMapping<TodoListViewModel, TodoListView>();
            RegisterMapping<TodoViewModel, TodoView>();
            RegisterMapping<SQLConsoleViewModel, SQLConsoleView>();
        }

        public void RegisterMapping<TViewModel, TView>()
            where TViewModel : class
            where TView : class
        {
            _viewModelToViewMappings[typeof(TViewModel)] = typeof(TView);
        }

        public void Navigate<T>()
            where T : class
        {
            Navigate<T>(null);
        }

        public void Navigate<T>(object? parameter)
            where T : class
        {
            var viewModelType = typeof(T);

            try
            {
                if (!_viewModelToViewMappings.TryGetValue(viewModelType, out var viewType))
                {
                    throw new InvalidOperationException(
                        $"No view mapping found for ViewModel {viewModelType.FullName}"
                    );
                }

                var viewModel = _serviceProvider.GetRequiredService<T>();

                // If the view model is of type TodoViewModel, set the parameter
                if (viewModel is TodoViewModel todoViewModel && parameter is TodoList list)
                {
                    // Pass the selected TodoList to the TodoViewModel
                    todoViewModel.SetList(list);
                }

                var view = _serviceProvider.GetRequiredService(viewType);

                if (view == null)
                {
                    throw new InvalidOperationException(
                        $"Could not resolve view {viewType.FullName}"
                    );
                }

                // Handle both Page and UserControl
                if (view is Page page)
                {
                    page.DataContext = viewModel;
                    _frame.Content = page;
                }
                else if (view is UserControl userControl)
                {
                    userControl.DataContext = viewModel;
                    _frame.Content = userControl;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"View {viewType.FullName} must be either a Page or UserControl"
                    );
                }

                // If the ViewModel implements INavigationAware, call OnNavigatedTo
                if (viewModel is INavigationAware navigationAware)
                {
                    navigationAware.OnNavigatedTo(parameter!);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Navigation error for {viewModelType.FullName}: {ex.Message}",
                    ex
                );
            }
        }

        public void GoBack()
        {
            if (_frame.CanGoBack)
            {
                _frame.GoBack();
            }
        }
    }

    public interface INavigationAware
    {
        void OnNavigatedTo(object parameter);
    }
}
