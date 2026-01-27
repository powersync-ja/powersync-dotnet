using System.Collections;
namespace PowerSync.Common.Utils;

public class Equality
{
    /// <summary>
    /// Performs a deep equality comparison between two objects.
    /// Handles dictionaries, lists, and primitive types.
    /// </summary>
    public static bool DeepEquals(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        if (a is IDictionary dictA && b is IDictionary dictB)
        {
            if (dictA.Count != dictB.Count) return false;
            foreach (DictionaryEntry kvp in dictA)
            {
                if (!dictB.Contains(kvp.Key)) return false;
                if (!DeepEquals(kvp.Value, dictB[kvp.Key])) return false;
            }
            return true;
        }

        if (a is IList listA && b is IList listB)
        {
            if (listA.Count != listB.Count) return false;
            for (int i = 0; i < listA.Count; i++)
            {
                if (!DeepEquals(listA[i], listB[i])) return false;
            }
            return true;
        }

        return a.Equals(b);
    }
}
