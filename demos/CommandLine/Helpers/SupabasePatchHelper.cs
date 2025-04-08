namespace CommandLine.Helpers;

using System.Linq.Expressions;
using Newtonsoft.Json;
using Supabase.Postgrest.Interfaces;
using Supabase.Postgrest.Models;

public static class SupabasePatchHelper
{
    // Applies a "SET" operation to the table, setting the value of a specific property.
    public static IPostgrestTable<T> ApplySet<T>(
        IPostgrestTable<T> table, // The table to apply the operation to
        string jsonPropertyName,  // The name of the JSON property to update
        object value              // The new value to set for the property
    ) where T : BaseModel, new() // Ensures T is a subclass of BaseModel with a parameterless constructor
    {
        // Find the property on the model that matches the JSON property name
        var property = typeof(T)
            .GetProperties()  // Get all properties of the model type
            .FirstOrDefault(p =>
                // Check if the property has a JsonPropertyAttribute
                p.GetCustomAttributes(typeof(JsonPropertyAttribute), true)
                .FirstOrDefault() is JsonPropertyAttribute attr &&
                attr.PropertyName == jsonPropertyName);  // Check if the JSON property name matches

        if (property == null)
            throw new ArgumentException($"'{jsonPropertyName}' is not a valid property on type '{typeof(T).Name}'");

        // Create an expression to access the specified property on the model
        var parameter = Expression.Parameter(typeof(T), "x"); // Define a parameter for the expression
        var propertyAccess = Expression.Property(parameter, property.Name); // Access the property
        var converted = Expression.Convert(propertyAccess, typeof(object)); // Convert the value to object type
        var lambda = Expression.Lambda<Func<T, object>>(converted, parameter); // Create a lambda expression for the property

        // Apply the "SET" operation to the table using the lambda expression
        return table.Set(lambda, value);
    }
}
