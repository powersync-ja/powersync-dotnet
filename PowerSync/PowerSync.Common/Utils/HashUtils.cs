namespace PowerSync.Common.Utils;

using System;
using System.Collections.Generic;

public static class HashUtils
{
    /// <summary>
    /// Create a hash from the key-value pairs in a dictionary. The hash does not depend
    /// on the internal pair ordering of the dictionary.
    /// </summary>
    public static int GetHashCodeDictionary<TKey, TValue>(Dictionary<TKey, TValue>? dict)
    {
        if (dict == null)
        {
            return 0;
        }

        // Use integer hash because order matters with System.HashCode
        int hash = 0;
        foreach (var kvp in dict)
        {
            // Combine hashes with XOR so that order doesn't matter
            hash ^= HashCode.Combine(kvp.Key, kvp.Value);
        }
        return hash;
    }

    /// <summary>
    /// Create a hash from an array of values. The hash depends on the order of
    /// elements in the array.
    /// </summary>
    public static int GetHashCodeArray<TValue>(TValue[]? values)
    {
        if (values == null)
        {
            return 0;
        }

        HashCode hash = new HashCode();
        foreach (var value in values)
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }
}
