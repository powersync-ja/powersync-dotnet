using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

using Newtonsoft.Json;

using PowerSync.Common.Client;

using PowersyncDotnetTodoList.Services;

namespace PowersyncDotnetTodoList.ViewModels
{
    public class SQLConsoleViewModel : ViewModelBase
    {
        #region Fields
        private readonly PowerSyncDatabase _db;
        private readonly INavigationService _navigationService;
        #endregion

        #region Properties
        private string _sqlQuery = "SELECT * FROM lists LIMIT 10;";
        public string SqlQuery
        {
            get => _sqlQuery;
            set
            {
                if (_sqlQuery != value)
                {
                    _sqlQuery = value;
                    OnPropertyChanged();
                }
            }
        }

        private ObservableCollection<object> _queryResults = new();
        public ObservableCollection<object> QueryResults
        {
            get => _queryResults;
            set
            {
                _queryResults = value;
                OnPropertyChanged();
            }
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                _errorMessage = value;
                OnPropertyChanged();
            }
        }
        #endregion

        #region Commands
        public ICommand ExecuteQueryCommand { get; }
        public ICommand BackCommand { get; }
        #endregion

        #region Constructor
        public SQLConsoleViewModel(IPowerSyncDatabase db, INavigationService navigationService)
        {
            _db =
                db as PowerSyncDatabase
                ?? throw new InvalidCastException("Expected PowerSyncDatabase instance.");
            _navigationService = navigationService;

            ExecuteQueryCommand = new RelayCommand(async () => await ExecuteQuery());
            BackCommand = new RelayCommand(GoBack);

            _ = ExecuteQuery();
        }
        #endregion

        #region Methods
        private async Task ExecuteQuery()
        {
            if (string.IsNullOrWhiteSpace(SqlQuery))
                return;

            try
            {
                ErrorMessage = string.Empty;

                // Fetch results from database
                var results = await _db.GetAll<object>(SqlQuery, []);

                // Update the collection with the results
                QueryResults = new ObservableCollection<object>(results);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                QueryResults = [];
            }
        }

        private void GoBack()
        {
            _navigationService.GoBack();
        }
        #endregion
    }
}
