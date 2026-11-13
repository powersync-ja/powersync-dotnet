using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using System.Windows.Input;

using PowerSync.Common.Client;
using PowerSync.Common.DB;

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

        private DataView _queryResults = new DataTable().DefaultView;
        public DataView QueryResults
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
                var results = await _db.GetAll(SqlQuery, []);

                // Update the grid with the results
                QueryResults = ToDataTable(results).DefaultView;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                QueryResults = new DataTable().DefaultView;
            }
        }

        private static DataTable ToDataTable(QueryResult results)
        {
            var table = new DataTable();
            if (results.Count == 0)
                return table;

            var columns = results.Rows[0].Columns;
            var duplicates = columns
                .GroupBy(column => column, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            if (duplicates.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Duplicate column names in result: {string.Join(", ", duplicates)}. Use aliases (e.g. SELECT a.id AS a_id) to give each column a unique name.");
            }

            for (var i = 0; i < columns.Count; i++)
            {
                table.Columns.Add(new DataColumn($"c{i}", typeof(object)) { Caption = columns[i] });
            }

            foreach (var row in results.Rows)
            {
                table.Rows.Add(columns.Select(column => row.GetValue(column) ?? DBNull.Value).ToArray());
            }

            return table;
        }

        private void GoBack()
        {
            _navigationService.GoBack();
        }
        #endregion
    }
}
