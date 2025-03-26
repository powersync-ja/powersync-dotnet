namespace PowerSync.Common.DB.Schema;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Schema(Dictionary<string, Table> tables)
{
    private readonly Dictionary<string, Table> Tables = tables;

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
