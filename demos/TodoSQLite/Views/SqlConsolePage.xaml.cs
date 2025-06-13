using System.Collections.ObjectModel;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TodoSQLite.Data;

namespace TodoSQLite.Views;

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
            
            var results = await database.Db.GetAll<object>(query);
            
            var keys =  JObject.Parse(JsonConvert.SerializeObject(results[0])).Properties().Select(p => p.Name).ToList();
            var allValues = results
                .Select(result => JObject.Parse(JsonConvert.SerializeObject(result))
                    .Properties()
                    .Select(p => p.Value.ToObject<object>())
                    .ToList())
                .ToList();
            
            Console.WriteLine($"Results count: {JsonConvert.SerializeObject(keys)}");
            Console.WriteLine($"Results count \n: {JsonConvert.SerializeObject(allValues)}");

            Headers.Text = string.Join(" | ", keys);
            Results.Text = string.Join("\n\n", allValues.Select(v => string.Join(" | ", v)));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }
} 