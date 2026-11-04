using Content.Shared._KS14.Fluidmos.Serialization;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Fluidmos;

/// <summary>
///     Reagents carried inside a <see cref="Content.Shared.Atmos.GasMixture"/>, as solid, liquid and (unlinked) vapor.
/// </summary>
/// <remarks>
///     <para>
///         The linear operations (split, merge, scale) need no prototypes. The phase totals are kept exact by
///             <see cref="KsFluidReagentTable"/> on every mutation.
///     </para>
///     <para>
///         The heat capacity needs each reagent's properties, so it is cached against the table's version and refreshed
///             through <see cref="GetHeatCapacity"/>, which takes the property table. Merges and scales carry the cache
///             across when both sides have one.
///     </para>
///     <para>
///         Solids never move with <see cref="RemoveRatio"/>: a pump drawing from a slurry takes the liquid and leaves
///             the ice. They do split with <see cref="Add"/> at a fraction, which is how a dividing pipe net shares them.
///     </para>
/// </remarks>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class KsFluidMixture : IEquatable<KsFluidMixture>
{
    [DataField(customTypeSerializer: typeof(KsFluidReagentsSerializer))]
    public KsFluidReagentTable Reagents = new();

    /// <summary>
    ///     Unscaled J/K of everything in the mixture, valid while <see cref="_heatCapacityVersion"/> matches the table.
    /// </summary>
    private float _heatCapacity;

    private int _heatCapacityVersion = -1;

    /// <summary>
    ///     The table's version when the last phase step finished.
    /// </summary>
    private int _phaseVersion = -1;

    /// <summary>
    ///     Temperature at the last phase step, for skipping quiescent mixtures.
    /// </summary>
    [ViewVariables]
    public float LastTemperature = float.NaN;

    /// <summary>
    ///     Pressure at the last phase step, for skipping quiescent mixtures.
    /// </summary>
    [ViewVariables]
    public float LastPressure = float.NaN;

    /// <summary>
    ///     Whether the last phase step changed next to nothing, so an unchanged mixture can skip the next one.
    /// </summary>
    [ViewVariables]
    public bool PhaseSettled;

    /// <summary>
    ///     When the last phase step ran, for its time step.
    /// </summary>
    [ViewVariables]
    public TimeSpan? LastPhaseTime;

    public KsFluidMixture()
    {
    }

    public KsFluidMixture(int capacity)
    {
        Reagents = new KsFluidReagentTable(capacity);
    }

    /// <summary>
    ///     Whether reagents have entered, left or changed phase since the last phase step finished.
    /// </summary>
    [ViewVariables]
    public bool CompositionChanged => Reagents.Version != _phaseVersion;

    [ViewVariables]
    public float LiquidUnits => Reagents.LiquidTotal;

    [ViewVariables]
    public float SolidUnits => Reagents.SolidTotal;

    /// <summary>
    ///     Liquid and solid together: everything that takes up volume.
    /// </summary>
    [ViewVariables]
    public float CondensedUnits => Reagents.LiquidTotal + Reagents.SolidTotal;

    /// <summary>
    ///     Moles of unlinked vapor. Linked vapor is in the gas mixture's moles instead.
    /// </summary>
    [ViewVariables]
    public float VaporMoles => Reagents.VaporTotal;

    [ViewVariables]
    public bool IsEmpty => Reagents.Count == 0;

    public int Count => Reagents.Count;

    /// <summary>
    ///     Records that a phase step has just finished, so <see cref="CompositionChanged"/> reads false until the next
    ///         change.
    /// </summary>
    public void MarkPhaseStepped()
    {
        _phaseVersion = Reagents.Version;
    }

    /// <summary>
    ///     Unscaled heat capacity, in J/K, of everything in the mixture.
    /// </summary>
    public float GetHeatCapacity(KsFluidmosPropertiesSystem propertiesSystem)
    {
        if (_heatCapacityVersion == Reagents.Version)
            return _heatCapacity;

        var heatCapacity = 0f;
        foreach (var (key, amount) in Reagents)
        {
            if (!propertiesSystem.TryGetProperties(key.Prototype, out var properties))
            {
                heatCapacity += amount.Solid + amount.Liquid;
                continue;
            }

            heatCapacity += (amount.Solid + amount.Liquid) * properties.SpecificHeat;
            if (amount.Vapor > 0f)
                heatCapacity += amount.Vapor / properties.MolesPerUnit * properties.VaporHeatCapacityPerUnit;
        }

        SetHeatCapacity(heatCapacity);
        return heatCapacity;
    }

    /// <summary>
    ///     Marks the cached heat capacity stale, after a change it cannot follow.
    /// </summary>
    public void InvalidateHeatCapacity()
    {
        _heatCapacityVersion = -1;
    }

    private bool HasHeatCapacity => _heatCapacityVersion == Reagents.Version;

    private void SetHeatCapacity(float heatCapacity)
    {
        _heatCapacity = heatCapacity;
        _heatCapacityVersion = Reagents.Version;
    }

    public bool TryGetAmount(KsFluidReagentKey key, out KsFluidAmount amount)
    {
        return Reagents.TryGetAmount(key, out amount);
    }

    /// <summary>
    ///     Replaces one reagent's amounts, dropping it when they fall below the minimums.
    /// </summary>
    public void SetAmount(KsFluidReagentKey key, KsFluidAmount amount)
    {
        Reagents.Set(key, amount);
    }

    /// <summary>
    ///     Adds units of liquid of one reagent.
    /// </summary>
    public void AddLiquid(KsFluidReagentKey key, float units)
    {
        if (units <= 0f)
            return;

        Reagents.TryGetAmount(key, out var amount);
        amount.Liquid += units;
        Reagents.Set(key, amount);
    }

    /// <summary>
    ///     Splits off <paramref name="ratio"/> of the liquid and vapor into a new mixture. Solids stay.
    /// </summary>
    /// <param name="includeLiquid">False takes vapor only, as a gas-only draw does.</param>
    public KsFluidMixture RemoveRatio(float ratio, bool includeLiquid = true)
    {
        return new KsFluidMixture { Reagents = Reagents.Split(ratio, includeLiquid: includeLiquid, includeVapor: true) };
    }

    /// <summary>
    ///     Splits off <paramref name="ratio"/> of the liquid only into a new mixture.
    /// </summary>
    public KsFluidMixture RemoveLiquidRatio(float ratio)
    {
        return new KsFluidMixture { Reagents = Reagents.Split(ratio, includeLiquid: true, includeVapor: false) };
    }

    /// <summary>
    ///     Adds <paramref name="fraction"/> of every phase of <paramref name="other"/>, which is not modified.
    /// </summary>
    public void Add(KsFluidMixture other, float fraction = 1f)
    {
        if (fraction <= 0f || other.IsEmpty)
            return;

        var carryHeatCapacity = HasHeatCapacity && other.HasHeatCapacity;
        var heatCapacity = _heatCapacity + other._heatCapacity * fraction;

        Reagents.AddScaled(other.Reagents, fraction);

        if (carryHeatCapacity)
            SetHeatCapacity(heatCapacity);
    }

    /// <summary>
    ///     Scales every phase, solids included, as deleting part of a pipe net does.
    /// </summary>
    public void Multiply(float multiplier)
    {
        var carryHeatCapacity = HasHeatCapacity;
        var heatCapacity = _heatCapacity * MathF.Max(multiplier, 0f);

        Reagents.Scale(multiplier);

        if (carryHeatCapacity)
            SetHeatCapacity(heatCapacity);
    }

    public void Clear()
    {
        Reagents.Clear();
        SetHeatCapacity(0f);
    }

    public void CopyFrom(KsFluidMixture other)
    {
        Reagents.CopyFrom(other.Reagents);

        if (other.HasHeatCapacity)
            SetHeatCapacity(other._heatCapacity);
    }

    public KsFluidMixture Clone()
    {
        var clone = new KsFluidMixture(Math.Max(Reagents.Count, 1));
        clone.CopyFrom(this);
        return clone;
    }

    /// <summary>
    ///     Adds a solution's reagents as liquid. The caller merges the temperature.
    /// </summary>
    public void AddSolution(Solution solution)
    {
        foreach (var reagentQuantity in solution.Contents)
        {
            AddLiquid(KsFluidReagentKey.From(reagentQuantity.Reagent), (float)reagentQuantity.Quantity);
        }
    }

    /// <summary>
    ///     Takes up to <paramref name="units"/> of liquid, spread across the reagents in proportion, as a solution. What
    ///         <see cref="FixedPoint2"/> truncates stays in the mixture. The caller sets the temperature.
    /// </summary>
    public Solution TakeLiquidAsSolution(float units)
    {
        var liquidUnits = LiquidUnits;
        var solution = new Solution();
        if (units <= 0f || liquidUnits <= 0f)
            return solution;

        var ratio = MathF.Min(units / liquidUnits, 1f);
        for (var i = Reagents.Count - 1; i >= 0; i--)
        {
            var key = Reagents.GetKey(i);
            var amount = Reagents.GetAmount(i);
            var quantity = FixedPoint2.New(amount.Liquid * ratio);
            if (quantity <= FixedPoint2.Zero)
                continue;

            solution.AddReagent(key.ToReagentId(), quantity);
            amount.Liquid -= (float)quantity;
            Reagents.Set(key, amount);
        }

        return solution;
    }

    /// <summary>
    ///     Takes all the solid as a solution. The caller sets the temperature.
    /// </summary>
    public Solution TakeSolidAsSolution()
    {
        var solution = new Solution();
        for (var i = Reagents.Count - 1; i >= 0; i--)
        {
            var key = Reagents.GetKey(i);
            var amount = Reagents.GetAmount(i);
            var quantity = FixedPoint2.New(amount.Solid);
            if (quantity <= FixedPoint2.Zero)
                continue;

            solution.AddReagent(key.ToReagentId(), quantity);
            amount.Solid -= (float)quantity;
            Reagents.Set(key, amount);
        }

        return solution;
    }

    /// <summary>
    ///     Takes up to <paramref name="moles"/> of unlinked vapor, in proportion, as a solution of the units it came from.
    /// </summary>
    public Solution TakeVaporAsSolution(float moles, KsFluidmosPropertiesSystem propertiesSystem)
    {
        var vaporMoles = VaporMoles;
        var solution = new Solution();
        if (moles <= 0f || vaporMoles <= 0f)
            return solution;

        var ratio = MathF.Min(moles / vaporMoles, 1f);
        for (var i = Reagents.Count - 1; i >= 0; i--)
        {
            var key = Reagents.GetKey(i);
            var amount = Reagents.GetAmount(i);
            if (amount.Vapor <= 0f)
                continue;

            var molesPerUnit = propertiesSystem.TryGetProperties(key.Prototype, out var properties)
                ? properties.MolesPerUnit
                : KsPhaseMath.MolesPerUnit(KsFluidmos.DefaultMolarMass);
            var quantity = FixedPoint2.New(amount.Vapor * ratio / molesPerUnit);
            if (quantity <= FixedPoint2.Zero)
                continue;

            solution.AddReagent(key.ToReagentId(), quantity);
            amount.Vapor = MathF.Max(0f, amount.Vapor - (float)quantity * molesPerUnit);
            Reagents.Set(key, amount);
        }

        return solution;
    }

    public bool Equals(KsFluidMixture? other)
    {
        if (ReferenceEquals(this, other))
            return true;

        return other != null && Reagents.ContentEquals(other.Reagents);
    }

    public override bool Equals(object? obj)
    {
        return obj is KsFluidMixture other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Reagents.Count, Reagents.LiquidTotal, Reagents.SolidTotal, Reagents.VaporTotal);
    }
}
