using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

using PowerSync.Common.Client;

using PowersyncDotnetTodoList.Models;
using PowersyncDotnetTodoList.Services;
using PowersyncDotnetTodoList.Views;

namespace PowersyncDotnetTodoList.ViewModels
{
    public class TodoListViewModel : ViewModelBase
    {
        #region Fields
        private readonly PowerSyncDatabase _db;
        private readonly PowerSyncConnector _connector;
        private readonly INavigationService _navigationService;
        #endregion

        #region Properties
        public ObservableCollection<TodoList> TodoLists { get; } = [];
        private TodoList? _selectedList;

        public TodoList? SelectedList
        {
            get => _selectedList;
            set
            {
                if (_selectedList != value)
                {
                    _selectedList = value;
                    OnPropertyChanged();
                    OpenList(_selectedList);
                }
            }
        }

        private string _newListName = "";
        public string NewListName
        {
            get => _newListName;
            set
            {
                if (_newListName != value)
                {
                    _newListName = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        #region Commands
        public ICommand AddListCommand { get; }
        public ICommand DeleteListCommand { get; }

        public ICommand SQLConsoleCommand { get; }
        #endregion

        #region Constructor
        public TodoListViewModel(
            PowerSyncDatabase db,
            PowerSyncConnector connector,
            INavigationService navigationService
        )
        {
            _db = db;
            _connector = connector;
            _navigationService = navigationService;

            AddListCommand = new RelayCommand<string>(
                async (newListName) =>
                {
                    if (!string.IsNullOrWhiteSpace(newListName))
                    {
                        await AddList(newListName);
                    }
                }
            );

            DeleteListCommand = new RelayCommand<TodoList>(
                async (list) =>
                {
                    if (list != null)
                    {
                        await DeleteList(list);
                    }
                }
            );
            SQLConsoleCommand = new RelayCommand(GoToSQLConsole);

            WatchForChanges();
            LoadTodoLists();
        }
        #endregion

        #region Methods
        private async void LoadTodoLists()
        {
            var query =
                @"
                SELECT 
                    l.*, 
                    COUNT(t.id) AS total_tasks, 
                    SUM(CASE WHEN t.completed = 1 THEN 1 ELSE 0 END) AS CompletedTasks,
                    SUM(CASE WHEN t.completed = 0 THEN 1 ELSE 0 END) AS PendingTasks,
                    MAX(t.completed_at) AS last_completed_at
                FROM 
                    lists l
                LEFT JOIN todos t
                    ON l.id = t.list_id
                GROUP BY 
                    l.id
                ORDER BY 
                    last_completed_at DESC NULLS LAST;
            ";

            var lists = await _db.GetAll<TodoListWithStats>(query);
            TodoLists.Clear();
            if (lists != null && lists.Any())
            {
                foreach (var list in lists)
                {
                    TodoLists.Add(list);
                }
            }
        }

        private async void WatchForChanges()
        {
            var query =
                @"
                SELECT 
                    l.*, 
                    COUNT(t.id) AS total_tasks, 
                    SUM(CASE WHEN t.completed = 1 THEN 1 ELSE 0 END) AS CompletedTasks,
                    SUM(CASE WHEN t.completed = 0 THEN 1 ELSE 0 END) AS PendingTasks,
                    MAX(t.completed_at) AS last_completed_at
                FROM 
                    lists l
                LEFT JOIN todos t
                    ON l.id = t.list_id
                GROUP BY 
                    l.id
                ORDER BY 
                    last_completed_at DESC NULLS LAST;
                ";

            await _db.Watch(
                query,
                null,
                new WatchHandler<TodoListWithStats>
                {
                    OnResult = (results) =>
                    {
                        var dispatcher = System.Windows.Application.Current?.Dispatcher;
                        if (dispatcher != null)
                        {
                            dispatcher.Invoke(() =>
                            {
                                TodoLists.Clear();
                                foreach (var result in results)
                                {
                                    TodoLists.Add(result);
                                }
                            });
                        }
                    },
                    OnError = (error) =>
                    {
                        Console.WriteLine("Error: " + error.Message);
                    },
                }
            );
        }

        private async Task AddList(string newListName)
        {
            await _db.Execute(
                "INSERT INTO lists (id, name, owner_id, created_at) VALUES (uuid(), ?, ?, datetime());",
                [newListName, _connector!.UserId]
            );

            NewListName = "";
        }

        private async Task DeleteList(TodoList list)
        {
            await _db.Execute("DELETE FROM lists WHERE id = ?;", [list.Id]);
            TodoLists.Remove(list);
        }

        private void OpenList(TodoList? selectedList)
        {
            if (selectedList != null)
            {
                _navigationService.Navigate<TodoViewModel>(selectedList);
            }
        }

        private void GoToSQLConsole()
        {
            // Navigate back to the SQLConsole View
            _navigationService.Navigate<SQLConsoleViewModel>();
        }
        #endregion
    }
}
