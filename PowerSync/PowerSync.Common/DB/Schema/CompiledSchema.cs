namespace PowerSync.Common.DB.Schema;

using Newtonsoft.Json;

class CompiledSchema(Dictionary<string, CompiledTable> tables)
{
    private readonly Dictionary<string, CompiledTable> Tables = tables;

    public void Validate()
    {
        foreach (var kvp in Tables)
        {
            var tableName = kvp.Key;
            var table = kvp.Value;

            if (CompiledTable.InvalidSQLCharacters.IsMatch(tableName))
            {
                throw new Exception($"Invalid characters in table name: {tableName}");
            }

            table.Validate();
        }
    }

    public string ToJSON()
    {
        var jsonObject = new
        {
            tables = Tables.Select(kvp => kvp.Value.ToJSONObject()).ToArray(),
        };

        return JsonConvert.SerializeObject(jsonObject);
    }
}
