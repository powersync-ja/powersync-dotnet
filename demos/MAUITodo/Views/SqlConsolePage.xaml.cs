using System.Collections.ObjectModel;
using System.Reflection;

using MAUITodo.Data;

namespace MAUITodo.Views;

public partial class SqlConsolePage : ContentPage
{
    private readonly PowerSyncData database;

    public SqlConsolePage(PowerSyncData powerSyncData)
    {
        InitializeComponent();
        database = powerSyncData;
    }

    private async void OnQuerySubmitted(object sender, EventArgs e)
    {
        Headers.Text = "";
        Results.Text = "";
        try
        {
            var query = QueryEntry.Text;
            if (string.IsNullOrWhiteSpace(query))
                return;

            var results = await database.Db.GetAll(query);
            if (results.Count == 0)
            {
                Headers.Text = "No results found.";
                Results.Text = "";
                return;
            }

            var columns = results.Rows[0].Columns;

            Headers.Text = string.Join(" | ", columns);
            Results.Text = string.Join("\n\n", results.Rows.Select(row => string.Join(" | ", columns.Select(row.GetValue))));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }
}
