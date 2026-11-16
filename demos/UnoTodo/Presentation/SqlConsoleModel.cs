using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnoTodo.Presentation;

public partial record SqlConsoleModel(PowerSyncData Data)
{
    public IState<string> Query => State<string>.Value(this, () => "SELECT * FROM lists");
    public IState<string> Headers => State<string>.Value(this, () => string.Empty);
    public IState<string> Results => State<string>.Value(this, () => string.Empty);

    public async ValueTask Execute()
    {
        var query = await Query;

        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            var rows = await Data.Db.GetAll<object>(query);
            if (rows.Length == 0)
            {
                await Headers.SetAsync("No results found.");
                await Results.SetAsync(string.Empty);
                return;
            }

            var keys = JObject.Parse(JsonConvert.SerializeObject(rows[0])).Properties().Select(p => p.Name).ToList();
            var allValues = rows
                .Select(row => JObject.Parse(JsonConvert.SerializeObject(row))
                    .Properties()
                    .Select(p => p.Value.ToObject<object>())
                    .ToList())
                .ToList();

            await Headers.SetAsync(string.Join(" | ", keys));
            await Results.SetAsync(string.Join("\n\n", allValues.Select(v => string.Join(" | ", v))));
        }
        catch (Exception ex)
        {
            await Headers.SetAsync("Error");
            await Results.SetAsync(ex.Message);
        }
    }
}
