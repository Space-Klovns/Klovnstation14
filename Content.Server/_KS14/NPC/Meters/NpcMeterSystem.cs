using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Meters;

/// <summary>
///     Reads and changes NPC meters (see <see cref="NpcMeterPrototype"/>). Lazy, like a battery's charge: a change
///         stores the value it leaves the meter at and when, and a read works out how far it has decayed since. There is
///         no update loop.
/// </summary>
public sealed partial class NpcMeterSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;

    [Dependency] private EntityQuery<NpcMetersComponent> _metersQuery = default!;

    /// <summary>
    ///     <paramref name="meterId"/>'s current value on <paramref name="entity"/>: 0 if it has never been raised there,
    ///         or is not a meter at all.
    /// </summary>
    public float GetValue(Entity<NpcMetersComponent?> entity, ProtoId<NpcMeterPrototype> meterId)
    {
        if (!_metersQuery.Resolve(entity.Owner, ref entity.Comp, false) ||
            !entity.Comp.Readings.TryGetValue(meterId, out var reading) ||
            !ProtoMan.TryIndex(meterId, out var meter))
            return 0f;

        return Decay(meter, reading, _gameTiming.CurTime);
    }

    /// <summary>
    ///     <paramref name="meterId"/>'s current value as a share of its maximum, from 0 to 1.
    /// </summary>
    public float GetFraction(Entity<NpcMetersComponent?> entity, ProtoId<NpcMeterPrototype> meterId)
    {
        if (!ProtoMan.TryIndex(meterId, out var meter) || meter.Max <= 0f)
            return 0f;

        return GetValue(entity, meterId) / meter.Max;
    }

    /// <summary>
    ///     Raises (or, with a negative <paramref name="amount"/>, lowers) <paramref name="meterId"/> on
    ///         <paramref name="uid"/>, from wherever it has decayed to by now, keeping it within 0 and its maximum.
    ///         Gives the NPC somewhere to keep meters if it has none.
    /// </summary>
    public void Add(EntityUid uid, ProtoId<NpcMeterPrototype> meterId, float amount)
    {
        if (!ProtoMan.TryIndex(meterId, out var meter))
            return;

        var metersComponent = EnsureComp<NpcMetersComponent>(uid);
        var now = _gameTiming.CurTime;
        var current = metersComponent.Readings.TryGetValue(meterId, out var reading) ? Decay(meter, reading, now) : 0f;

        metersComponent.Readings[meterId] = new NpcMeterReading(Math.Clamp(current + amount, 0f, meter.Max), now);
    }

    /// <summary>
    ///     Sets <paramref name="meterId"/> on <paramref name="uid"/> outright, within 0 and its maximum.
    /// </summary>
    public void Set(EntityUid uid, ProtoId<NpcMeterPrototype> meterId, float value)
    {
        if (!ProtoMan.TryIndex(meterId, out var meter))
            return;

        EnsureComp<NpcMetersComponent>(uid).Readings[meterId] = new NpcMeterReading(Math.Clamp(value, 0f, meter.Max), _gameTiming.CurTime);
    }

    /// <summary>
    ///     Every meter <paramref name="entity"/> has a reading for, with its current value and maximum, added to
    ///         <paramref name="results"/>. For debugging.
    /// </summary>
    public void GetAll(Entity<NpcMetersComponent?> entity, List<(ProtoId<NpcMeterPrototype> MeterId, float Value, float Max)> results)
    {
        if (!_metersQuery.Resolve(entity.Owner, ref entity.Comp, false))
            return;

        var now = _gameTiming.CurTime;
        foreach (var (meterId, reading) in entity.Comp.Readings)
        {
            if (ProtoMan.TryIndex(meterId, out var meter))
                results.Add((meterId, Decay(meter, reading, now), meter.Max));
        }
    }

    private static float Decay(NpcMeterPrototype meter, NpcMeterReading reading, TimeSpan now)
    {
        var elapsed = (float) (now - reading.TakenAt).TotalSeconds;
        return Math.Max(0f, reading.Value - meter.DecayPerSecond * Math.Max(0f, elapsed));
    }
}
