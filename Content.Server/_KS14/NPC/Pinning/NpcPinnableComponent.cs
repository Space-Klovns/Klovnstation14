using Content.Server._KS14.NPC.Meters;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Pinning;

/// <summary>
///     Opts an NPC in to being pinned down: while hostile fire passes close to where it stands, a meter
///         (<see cref="Meter"/>) climbs, and the HTN reads that meter to seek cover and peek less. Elites shrug it off by
///         having <see cref="Gain"/> 0 (or no component at all). See <see cref="NpcPinnedDownSystem"/>.
/// </summary>
/// <remarks>
///     <para>
///         The numbers are a pair with the HTN: the operative root gates its pinned branch on
///             <c>MeterPrecondition { meter: KsSuppressed, min: 60 }</c> (<c>gun.yml</c>, <c>root.yml</c>), and
///             <see cref="ReplanThreshold"/> is where this system asks for a replan so that is taken up at once. Keep the
///             two equal. The README's <i>Pinned down</i> table lists every setting.
///     </para>
///     <para>
///         The sense layer here is only the meter. What an NPC does about it is HTN (<c>gun.yml</c>'s first branch, the
///             <c>support.yml</c> and <c>peek.yml</c> gates), so a balance pass changes YAML, not this.
///     </para>
/// </remarks>
[RegisterComponent]
public sealed partial class NpcPinnableComponent : Component
{
    /// <summary>
    ///     The meter that rises while under fire.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<NpcMeterPrototype> Meter;

    /// <summary>
    ///     Added to <see cref="Meter"/> each check that finds the NPC under fire. 0 or less never pins it down: how an elite
    ///         shrugs it off. One stray round (two checks within <see cref="Memory"/>) leaves the meter well short of the
    ///         HTN's threshold; a burst at the NPC gets it there in about three checks, three quarters of a second.
    /// </summary>
    [DataField]
    public float Gain = 25f;

    /// <summary>
    ///     How close, in tiles, a line of fire passes for the NPC to count as under it: its own width and the gun's spread.
    /// </summary>
    [DataField]
    public float Clearance = 1f;

    /// <summary>
    ///     How recent a shot must be to count (<c>NpcGunfireSystem.IsUnderFire</c>'s memory). Not much more than
    ///         <see cref="Interval"/>, or one round is counted again at every check it outlives.
    /// </summary>
    [DataField]
    public TimeSpan Memory = TimeSpan.FromSeconds(0.4);

    /// <summary>
    ///     How often to look, per NPC: never per tick.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(0.25);

    /// <summary>
    ///     When the meter first reaches this, the NPC's plan is asked to look again at once, rather than on its next
    ///         replan. Equal to the HTN's <c>min</c> on the meter.
    /// </summary>
    [DataField]
    public float ReplanThreshold = 60f;

    [ViewVariables]
    public TimeSpan NextCheck;
}
