using System;
using System.Collections.Generic;

namespace Cheeze.Managed;

public interface IItemPort<T>
{
    bool Contains(T item);
    bool Remove(T item);
    bool Add(T item);
}

public static class ItemTransfer
{
    public static int MoveAvailable<T>(IItemPort<T> source, IItemPort<T> target,
        IEnumerable<T> items, Func<T, bool> canAccept, Action<T> committed)
    {
        int moved = 0;
        foreach (T item in items)
        {
            if (!canAccept(item) || !Move(source, target, item)) break;
            moved++;
            committed(item);
        }
        return moved;
    }

    // Never create copies of item instances. Check actual ownership even if an API
    // throws after completing a mutation, and restore a rejected transfer.
    public static bool Move<T>(IItemPort<T> source, IItemPort<T> target, T item)
    {
        if (!source.Contains(item) || target.Contains(item))
            throw new InvalidOperationException("Unexpected ownership before farm transfer.");
        try
        {
            if (!source.Remove(item) && source.Contains(item)) return false;
            if (source.Contains(item))
                throw new InvalidOperationException("Source did not remove the ingredient.");
            target.Add(item);
        }
        catch
        {
            // Result codes alone cannot tell whether the mutation completed.
            if (target.Contains(item) && !source.Contains(item)) return true;
            if (source.Contains(item) && !target.Contains(item)) return false;
            if (!target.Contains(item) && !source.Contains(item))
            {
                source.Add(item);
                if (source.Contains(item) && !target.Contains(item)) return false;
            }
            throw new InvalidOperationException("Could not restore ingredient ownership; farm supply stopped.");
        }
        if (target.Contains(item) && !source.Contains(item)) return true;
        if (source.Contains(item) && !target.Contains(item)) return false;
        if (!target.Contains(item))
        {
            source.Add(item);
            if (source.Contains(item)) return false;
        }
        throw new InvalidOperationException("Ingredient transfer invariant failed; farm supply stopped.");
    }
}
