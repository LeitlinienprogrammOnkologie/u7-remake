using System.Collections.Generic;

namespace U7.Data;

/// <summary>
/// Exult <c>Game_object::Game_object_set</c>, a <c>std::set&lt;Game_object*&gt;</c>
/// for the render dependencies: ordered by address, which in practice is the
/// order the objects were made in, here <see cref="U7Object.Id"/>'s. The order
/// decides which of two dependencies that don't depend on each other is
/// painted first (so what a translucent pixel shows of the other), and must be
/// the same in every run, as a hash set's is not. A sorted list: the sets are
/// small, and the painter walks them every frame without allocating.
/// </summary>
public sealed class ObjectSet
{
    static readonly Comparer<U7Object> ById = Comparer<U7Object>.Create((a, b) => a.Id.CompareTo(b.Id));

    readonly List<U7Object> _items = new();

    public int Count => _items.Count;

    public void Add(U7Object obj)
    {
        var at = _items.BinarySearch(obj, ById);
        if (at < 0)
        {
            _items.Insert(~at, obj);
        }
    }

    public void Remove(U7Object obj)
    {
        var at = _items.BinarySearch(obj, ById);
        if (at >= 0)
        {
            _items.RemoveAt(at);
        }
    }

    public void Clear() => _items.Clear();

    public List<U7Object>.Enumerator GetEnumerator() => _items.GetEnumerator();
}
