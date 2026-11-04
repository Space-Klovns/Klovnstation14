using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Fluidmos;

/// <summary>
///     A reagent's extra data (DNA, say), immutable and normalised once at the Solution boundary. Splits and merges share
///         the same instance, so key comparisons on the hot path are reference compares in practice.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsReagentDataSet : IEquatable<KsReagentDataSet>
{
    public readonly ReagentData[] Items;

    /// <summary>
    ///     Order-insensitive: the sum of the element hashes.
    /// </summary>
    private readonly int _hash;

    private KsReagentDataSet(ReagentData[] items)
    {
        Items = items;

        foreach (var item in items)
        {
            _hash = unchecked(_hash + item.GetHashCode());
        }
    }

    /// <summary>
    ///     Builds a set from a reagent's data list, cloning each element. Null or empty data is null.
    /// </summary>
    public static KsReagentDataSet? From(List<ReagentData>? data)
    {
        if (data == null || data.Count == 0)
            return null;

        var items = new ReagentData[data.Count];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = data[i].Clone();
        }

        return new KsReagentDataSet(items);
    }

    /// <summary>
    ///     A fresh, mutable list of cloned elements, for a <see cref="ReagentId"/>.
    /// </summary>
    public List<ReagentData> ToList()
    {
        var list = new List<ReagentData>(Items.Length);
        foreach (var item in Items)
        {
            list.Add(item.Clone());
        }

        return list;
    }

    public bool Equals(KsReagentDataSet? other)
    {
        if (ReferenceEquals(this, other))
            return true;

        if (other == null || _hash != other._hash || Items.Length != other.Items.Length)
            return false;

        foreach (var item in Items)
        {
            var found = false;
            foreach (var otherItem in other.Items)
            {
                if (!item.Equals(otherItem))
                    continue;

                found = true;
                break;
            }

            if (!found)
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is KsReagentDataSet other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _hash;
    }
}
