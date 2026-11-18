namespace PowerSync.Common.SourceGenerators;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

/// <summary>
/// An immutable array with value equality, so that generator models compare by content and
/// unchanged inputs are cached by the incremental pipeline.
/// </summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> array) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _array = array;

    public ImmutableArray<T> AsImmutableArray() => _array.IsDefault ? ImmutableArray<T>.Empty : _array;

    public bool Equals(EquatableArray<T> other) => AsImmutableArray().SequenceEqual(other.AsImmutableArray());

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in AsImmutableArray())
        {
            hash = (hash * 31) + (item?.GetHashCode() ?? 0);
        }
        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)AsImmutableArray()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
