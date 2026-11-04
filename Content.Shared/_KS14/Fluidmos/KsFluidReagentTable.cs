using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Fluidmos;

/// <summary>
///     The reagents in a <see cref="KsFluidMixture"/>: a small map from <see cref="KsFluidReagentKey"/> to
///         <see cref="KsFluidAmount"/> that keeps its solid, liquid and vapor totals exact on every mutation.
/// </summary>
/// <remarks>
///     <para>
///         Stored as parallel arrays rather than a dictionary. A mixture holds a handful of reagents, so a linear scan
///             (data sets compare by reference first) beats hashing a prototype id per lookup, and the phases being
///             contiguous lets splits, scales and totals run through <see cref="NumericsHelpers"/>' SIMD paths.
///     </para>
///     <para>
///         Every mutation bumps <see cref="Version"/>, which is how the mixture knows its cached heat capacity is stale
///             and whether its composition changed since the last phase step. Removal swaps the last entry into the
///             hole, so indices are only stable between mutations.
///     </para>
/// </remarks>
[Serializable, NetSerializable]
public sealed class KsFluidReagentTable
{
    private const int DefaultCapacity = 4;

    private KsFluidReagentKey[] _keys;
    private float[] _solid;
    private float[] _liquid;
    private float[] _vapor;
    private int _count;

    /// <summary>
    ///     Units of solid, across every reagent.
    /// </summary>
    public float SolidTotal { get; private set; }

    /// <summary>
    ///     Units of liquid, across every reagent.
    /// </summary>
    public float LiquidTotal { get; private set; }

    /// <summary>
    ///     Moles of unlinked vapor, across every reagent.
    /// </summary>
    public float VaporTotal { get; private set; }

    /// <summary>
    ///     Bumped by every mutation.
    /// </summary>
    public int Version { get; private set; }

    public KsFluidReagentTable() : this(DefaultCapacity)
    {
    }

    public KsFluidReagentTable(int capacity)
    {
        capacity = Math.Max(capacity, 1);
        _keys = new KsFluidReagentKey[capacity];
        _solid = new float[capacity];
        _liquid = new float[capacity];
        _vapor = new float[capacity];
    }

    public int Count => _count;

    public KsFluidReagentKey GetKey(int index)
    {
        return _keys[index];
    }

    public KsFluidAmount GetAmount(int index)
    {
        return new KsFluidAmount(_solid[index], _liquid[index], _vapor[index]);
    }

    public int IndexOf(in KsFluidReagentKey key)
    {
        for (var i = 0; i < _count; i++)
        {
            ref readonly var existing = ref _keys[i];
            if (existing.Prototype == key.Prototype &&
                (ReferenceEquals(existing.Data, key.Data) || Equals(existing.Data, key.Data)))
                return i;
        }

        return -1;
    }

    public bool ContainsKey(in KsFluidReagentKey key)
    {
        return IndexOf(key) >= 0;
    }

    public bool TryGetAmount(in KsFluidReagentKey key, out KsFluidAmount amount)
    {
        var index = IndexOf(key);
        if (index < 0)
        {
            amount = default;
            return false;
        }

        amount = GetAmount(index);
        return true;
    }

    /// <summary>
    ///     Replaces one reagent's amounts, dropping it when they all fall below the minimums.
    /// </summary>
    public void Set(in KsFluidReagentKey key, KsFluidAmount amount)
    {
        Trim(ref amount);
        var index = IndexOf(key);

        if (amount.IsEmpty)
        {
            if (index >= 0)
                RemoveAt(index);
        }
        else if (index >= 0)
        {
            Write(index, amount);
        }
        else
        {
            Append(key, amount);
        }

        Recount();
    }

    /// <summary>
    ///     Adds <paramref name="fraction"/> of every phase of <paramref name="other"/>, which is not modified.
    /// </summary>
    public void AddScaled(KsFluidReagentTable other, float fraction)
    {
        if (fraction <= 0f || other._count == 0)
            return;

        for (var i = 0; i < other._count; i++)
        {
            var added = new KsFluidAmount(other._solid[i] * fraction, other._liquid[i] * fraction, other._vapor[i] * fraction);
            var index = IndexOf(other._keys[i]);

            if (index >= 0)
            {
                _solid[index] += added.Solid;
                _liquid[index] += added.Liquid;
                _vapor[index] += added.Vapor;
                continue;
            }

            Trim(ref added);
            if (!added.IsEmpty)
                Append(other._keys[i], added);
        }

        Recount();
    }

    /// <summary>
    ///     Moves <paramref name="ratio"/> of the liquid and/or vapor into a new table. Solids never move.
    /// </summary>
    public KsFluidReagentTable Split(float ratio, bool includeLiquid, bool includeVapor)
    {
        var removed = new KsFluidReagentTable(Math.Max(_count, 1));
        if (ratio <= 0f || _count == 0 || !includeLiquid && !includeVapor)
            return removed;

        ratio = MathF.Min(ratio, 1f);
        Array.Copy(_keys, removed._keys, _count);
        removed._count = _count;

        if (includeLiquid)
            MoveRatio(_liquid, removed._liquid, ratio);

        if (includeVapor)
            MoveRatio(_vapor, removed._vapor, ratio);

        Compact();
        removed.Compact();
        return removed;
    }

    private void MoveRatio(float[] source, float[] destination, float ratio)
    {
        var sourceSpan = source.AsSpan(0, _count);
        var destinationSpan = destination.AsSpan(0, _count);
        NumericsHelpers.Multiply(sourceSpan, ratio, destinationSpan);
        NumericsHelpers.Sub(sourceSpan, destinationSpan);
    }

    /// <summary>
    ///     Scales every phase, solids included.
    /// </summary>
    public void Scale(float multiplier)
    {
        if (multiplier <= 0f)
        {
            Clear();
            return;
        }

        NumericsHelpers.Multiply(_solid.AsSpan(0, _count), multiplier);
        NumericsHelpers.Multiply(_liquid.AsSpan(0, _count), multiplier);
        NumericsHelpers.Multiply(_vapor.AsSpan(0, _count), multiplier);
        Compact();
    }

    public void Clear()
    {
        Array.Clear(_keys, 0, _count);
        _count = 0;
        Recount();
    }

    public void CopyFrom(KsFluidReagentTable other)
    {
        EnsureCapacity(other._count);
        Array.Clear(_keys, 0, _count);
        Array.Copy(other._keys, _keys, other._count);
        Array.Copy(other._solid, _solid, other._count);
        Array.Copy(other._liquid, _liquid, other._count);
        Array.Copy(other._vapor, _vapor, other._count);
        _count = other._count;
        Recount();
    }

    public KsFluidReagentTable Clone()
    {
        var clone = new KsFluidReagentTable(Math.Max(_count, 1));
        clone.CopyFrom(this);
        return clone;
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    /// <summary>
    ///     Drops amounts below the minimums and entries left empty, then recounts.
    /// </summary>
    private void Compact()
    {
        for (var i = _count - 1; i >= 0; i--)
        {
            var amount = GetAmount(i);
            Trim(ref amount);

            if (amount.IsEmpty)
                RemoveAt(i);
            else
                Write(i, amount);
        }

        Recount();
    }

    private void Recount()
    {
        SolidTotal = NumericsHelpers.HorizontalAdd(_solid.AsSpan(0, _count));
        LiquidTotal = NumericsHelpers.HorizontalAdd(_liquid.AsSpan(0, _count));
        VaporTotal = NumericsHelpers.HorizontalAdd(_vapor.AsSpan(0, _count));
        Version++;
    }

    private void Write(int index, KsFluidAmount amount)
    {
        _solid[index] = amount.Solid;
        _liquid[index] = amount.Liquid;
        _vapor[index] = amount.Vapor;
    }

    private void Append(in KsFluidReagentKey key, KsFluidAmount amount)
    {
        EnsureCapacity(_count + 1);
        _keys[_count] = key;
        Write(_count, amount);
        _count++;
    }

    /// <summary>
    ///     Swaps the last entry into <paramref name="index"/>. Does not recount.
    /// </summary>
    private void RemoveAt(int index)
    {
        var last = _count - 1;
        if (index != last)
        {
            _keys[index] = _keys[last];
            _solid[index] = _solid[last];
            _liquid[index] = _liquid[last];
            _vapor[index] = _vapor[last];
        }

        _keys[last] = default;
        _solid[last] = 0f;
        _liquid[last] = 0f;
        _vapor[last] = 0f;
        _count = last;
    }

    private void EnsureCapacity(int capacity)
    {
        if (_keys.Length >= capacity)
            return;

        var newCapacity = Math.Max(capacity, _keys.Length * 2);
        Array.Resize(ref _keys, newCapacity);
        Array.Resize(ref _solid, newCapacity);
        Array.Resize(ref _liquid, newCapacity);
        Array.Resize(ref _vapor, newCapacity);
    }

    private static void Trim(ref KsFluidAmount amount)
    {
        if (!(amount.Solid >= KsFluidmos.MinLiquidUnits))
            amount.Solid = 0f;

        if (!(amount.Liquid >= KsFluidmos.MinLiquidUnits))
            amount.Liquid = 0f;

        if (!(amount.Vapor >= KsFluidmos.MinVaporMoles))
            amount.Vapor = 0f;
    }

    public bool ContentEquals(KsFluidReagentTable other)
    {
        if (_count != other._count)
            return false;

        for (var i = 0; i < _count; i++)
        {
            var otherIndex = other.IndexOf(_keys[i]);
            if (otherIndex < 0 ||
                !_solid[i].Equals(other._solid[otherIndex]) ||
                !_liquid[i].Equals(other._liquid[otherIndex]) ||
                !_vapor[i].Equals(other._vapor[otherIndex]))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Enumerates <c>(Key, Amount)</c> pairs. Mutating the table while enumerating it is not supported.
    /// </summary>
    public struct Enumerator(KsFluidReagentTable table)
    {
        private int _index = -1;

        public bool MoveNext()
        {
            return ++_index < table._count;
        }

        public (KsFluidReagentKey Key, KsFluidAmount Amount) Current => (table._keys[_index], table.GetAmount(_index));
    }
}
