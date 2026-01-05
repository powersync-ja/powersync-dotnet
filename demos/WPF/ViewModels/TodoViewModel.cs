using System.Collections.ObjectModel;
using System.Windows.Input;

using PowerSync.Common.Client;

using PowersyncDotnetTodoList.Models;
using PowersyncDotnetTodoList.Services;

namespace PowersyncDotnetTodoList.ViewModels
{
    public class TodoViewModel : ViewModelBase
    {
        #region Fields
        private readonly PowerSyncDatabase _db;
        private readonly PowerSyncConnector _connector;
        private readonly INavigationService _navigationService;
        private TodoList? _list;
        #endregion

        #region Properties
        public ObservableCollection<Todo> Todos { get; } = new();

        private Todo? _selectedTodo;
        public Todo? SelectedTodo
        {
            get => _selectedTodo;
            set
            {
                _selectedTodo = value;
                OnPropertyChanged();
            }
        }

        private string _newTodoName = "";
        public string NewTodoName
        {
            get => _newTodoName;
            set
            {
                if (_newTodoName != value)
                {
                    _newTodoName = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _listName = "";
        public string ListName
        {
            get => _listName;
            set
            {
                _listName = value;
                OnPropertyChanged();
            }
        }
        #endregion

        #region Commands
        public ICommand AddTodoCommand { get; }
        public ICommand DeleteTodoCommand { get; }
        public ICommand ToggleCompleteCommand { get; }
        public ICommand BackCommand { get; }
        #endregion

        #region Constructor
        public TodoViewModel(
            IPowerSyncDatabase db,
            PowerSyncConnector connector,
            INavigationService navigationService
        )
        {
            _db =
                db as PowerSyncDatabase
                ?? throw new InvalidCastException("Expected PowerSyncDatabase instance.");
            _connector = connector;
            _navigationService = navigationService;

            AddTodoCommand = new RelayCommand<string>(
                async (newTodoName) =>
                {
                    if (!string.IsNullOrWhiteSpace(newTodoName))
                    {
                        await AddTodo(newTodoName);
                    }
                }
            );
            DeleteTodoCommand = new RelayCommand<Todo>(async (todo) => await DeleteTodo(todo));
            ToggleCompleteCommand = new RelayCommand<Todo>(
                async (todo) => await ToggleComplete(todo)
            );
            BackCommand = new RelayCommand(GoBack);
        }
        #endregion

        #region Methods
        public void SetList(TodoList list)
        {
            _list = list;
            _listName = list.Name;
            LoadTodos();
            WatchForChanges();
        }

        private async void WatchForChanges()
        {
            await _db.Watch(
                "SELECT * FROM todos where list_id = ? ORDER BY created_at;",
                [_list!.Id],
                new WatchHandler<Todo>
                {
                    OnResult = (results) =>
                    {
                        var dispatcher = System.Windows.Application.Current?.Dispatcher;
                        if (dispatcher != null)
                        {
                            dispatcher.Invoke(() =>
                            {
                                Todos.Clear();
                                foreach (var result in results)
                                {
                                    Todos.Add(result);
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

        private async void LoadTodos()
        {
            var todos = await _db.GetAll<Todo>(
                "SELECT * FROM todos where list_id = ? ORDER BY created_at;",
                [_list!.Id]
            );
            Todos.Clear();
            foreach (var todo in todos)
            {
                Todos.Add(todo);
            }
        }

        private async Task AddTodo(string todoName)
        {
            if (!string.IsNullOrWhiteSpace(todoName))
            {
                await _db.Execute(
                    "INSERT INTO todos (id, description, completed, created_at, list_id) VALUES (uuid(), ?, 0, datetime(), ?);",
                    [todoName, _list!.Id]
                );
                LoadTodos();
                NewTodoName = "";
            }
        }

        private async Task DeleteTodo(Todo todo)
        {
            await _db.Execute("DELETE FROM todos WHERE id = ?;", [todo.Id]);
            Todos.Remove(todo);
        }

        private async Task ToggleComplete(Todo todo)
        {
            // Toggle the completed state
            var newCompletionState = todo.Completed ? 1 : 0;

            // Update the database with the new completion state
            await _db.Execute(
                "UPDATE todos SET completed = ? WHERE id = ?;",
                [newCompletionState, todo.Id]
            );
        }

        private void GoBack()
        {
            // Navigate back to the TodoList view
            _navigationService.GoBack();
        }
        #endregion
    }
}
