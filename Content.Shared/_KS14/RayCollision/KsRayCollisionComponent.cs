using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.RayCollision;

/// <summary>
///     Has <see cref="KsRayCollisionSystem"/> look along where this entity is about to go each tick, before physics moves it,
///         for anything its <see cref="Fixture"/> would collide with on the way: so that something fast enough to step
///         clean over a thin thing between two physics steps, or onto it and what is behind it at once, still hits it.
///         Added and removed by the system's <see cref="KsRayCollisionSystem.StartChecking"/> and
///         <see cref="KsRayCollisionSystem.StopChecking"/>, not in YAML.
/// </summary>
[RegisterComponent]
[Access(typeof(KsRayCollisionSystem))]
public sealed partial class KsRayCollisionComponent : Component
{
    /// <summary>
    ///     The fixture whose way is looked along: what it collides with is what counts. Hard or not; a projectile's is not.
    /// </summary>
    [ViewVariables]
    public string Fixture = string.Empty;

    /// <summary>
    ///     What the look ahead has had it collide with on its way. Physics may yet start a contact with any of them, a step
    ///         later, which is not a second collision: see <see cref="KsRayCollisionSystem.WasSwept"/>. Forgotten once it
    ///         has left the thing behind (a tick on, and no longer touching it), or is no longer on its way: at rest, in a
    ///         container, or without its fixture. Each with the tick it was hit on, by
    ///         which a client running that tick again forgets it: see the system.
    /// </summary>
    [ViewVariables]
    public readonly List<(EntityUid Uid, GameTick Tick)> SweptHits = new();

    /// <summary>
    ///     Whether it has been looked ahead of since it was last at rest: the first look of a flight also collides it with
    ///         what its middle is already inside, which no ray from there finds.
    /// </summary>
    [ViewVariables]
    public bool Flying;
}

/// <summary>
///     Raised on an entity with <see cref="KsRayCollisionComponent"/> that is about to go through <paramref name="OtherUid"/>
///         this tick, the first thing on its way that its fixture collides with (by layer and mask, hard, and not prevented
///         by <c>PreventCollideEvent</c>, as physics would have it). It has been moved up to <paramref name="Point"/> on
///         <paramref name="OtherUid"/>'s side, so what follows happens where it would have.
/// </summary>
[ByRefEvent]
public readonly record struct KsRayCollisionEvent(EntityUid OtherUid, MapCoordinates Point);
