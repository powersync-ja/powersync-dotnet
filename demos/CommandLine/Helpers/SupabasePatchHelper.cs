namespace CommandLine.Helpers;

using System.Linq.Expressions;
using Newtonsoft.Json;
using Supabase.Postgrest.Interfaces;
using Supabase.Postgrest.Models;

public static class SupabasePatchHelper
{
    public static IPostgrestTable<T> ApplySet<T>(
        IPostgrestTable<T> table,
        string jsonPropertyName,
        object value
    ) where T : BaseModel, new()
    {
        // Find the property that matches the JsonProperty name
        var property = typeof(T)
            .GetProperties()
            .FirstOrDefault(p =>
                p.GetCustomAttributes(typeof(JsonPropertyAttribute), true)
                .FirstOrDefault() is JsonPropertyAttribute attr &&
                attr.PropertyName == jsonPropertyName);

        if (property == null)
            throw new ArgumentException($"'{jsonPropertyName}' is not a valid property on type '{typeof(T).Name}'");

        var parameter = Expression.Parameter(typeof(T), "x");
        var propertyAccess = Expression.Property(parameter, property.Name);
        var converted = Expression.Convert(propertyAccess, typeof(object));
        var lambda = Expression.Lambda<Func<T, object>>(converted, parameter);

        return table.Set(lambda, value);
    }
}