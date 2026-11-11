using System.Data;
using System.Windows.Controls;

namespace PowersyncDotnetTodoList.Views
{
    public partial class SQLConsoleView : Page
    {
        public SQLConsoleView()
        {
            InitializeComponent();
        }

        private void OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (sender is DataGrid { ItemsSource: DataView view } && view.Table?.Columns[e.PropertyName] is { } column)
            {
                e.Column.Header = column.Caption;
            }
        }
    }
}
