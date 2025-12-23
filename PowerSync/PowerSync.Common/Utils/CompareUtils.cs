namespace PowerSync.Common.Utils;

using System.Collections.Generic;
using System.Linq;

public static class CompareUtils
{
    /// <summary>
    /// Compare two dictionaries by value. Checks if both dictionaries have both the same 
    /// number of keys, as well as the same keys pointing to the same values.
    /// </summary>
    public static bool DictionariesEqual<TKey, TValue>(Dictionary<TKey, TValue>? dict1, Dictionary<TKey, TValue>? dict2)
    {
        if (ReferenceEquals(dict1, dict2)) return true;
        if (dict1 == null || dict2 == null) return false;
        if (dict1.Count != dict2.Count) return false;

        var comparer = EqualityComparer<TValue>.Default;

        foreach (var keyValuePair in dict1)
        {
            if (!dict2.TryGetValue(keyValuePair.Key, out TValue secondValue) ||
              !comparer.Equals(keyValuePair.Value, secondValue))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Check if two (maybe null) arrays contain the same elements in the same order.
    /// Effectively arr1.SequenceEqual(arr2), but with null checks.
    /// </summary>
    public static bool ArraysEqual<TValue>(TValue[]? arr1, TValue[]? arr2)
    {
        if (ReferenceEquals(arr1, arr2)) return true;
        if (arr1 == null || arr2 == null) return false;
        return arr1.SequenceEqual(arr2);
    }
}

