using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cocoar.SignalARRR.SourceGenerator.Helpers;

namespace Cocoar.SignalARRR.SourceGenerator.Model;

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _array;

    public EquatableArray(T[] array) => _array = array;

    public int Length => _array?.Length ?? 0;
    public T this[int index] => _array![index];

    public bool Equals(EquatableArray<T> other)
    {
        if (_array is null && other._array is null) return true;
        if (_array is null || other._array is null) return false;
        return _array.SequenceEqual(other._array);
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array is null) return 0;
        var hash = 17;
        foreach (var item in _array)
            hash = HashCombine.Combine(hash, HashCombine.Of(item));
        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_array ?? Array.Empty<T>())).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
