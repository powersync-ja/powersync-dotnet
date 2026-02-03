namespace PowerSync.Common.DB.Schema;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            tables = Tables.Select(kv =>
            {
                var json = JObject.Parse(kv.Value.ToJSON(kv.Key));
                var orderedJson = new JObject { ["name"] = kv.Key };
                orderedJson.Merge(json, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Concat });
                return orderedJson;
            }).ToList()
        };

        return JsonConvert.SerializeObject(jsonObject);
    }
}
