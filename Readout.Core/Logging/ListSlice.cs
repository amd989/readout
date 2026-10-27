using System.Collections;

namespace Readout.Core.Logging;

/// <summary>A fixed stretch of a list, read in place. The list may grow but must not shrink.</summary>
public sealed class ListSlice<T> : IReadOnlyList<T>
{
    private readonly List<T> _list;
    private readonly int _start;

    public ListSlice(List<T> list, int start, int count)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + count, list.Count, nameof(count));
        _list = list;
        _start = start;
        Count = count;
    }

    public int Count { get; }

    public T this[int index] =>
        (uint)index < (uint)Count ? _list[_start + index] : throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
            yield return _list[_start + i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
