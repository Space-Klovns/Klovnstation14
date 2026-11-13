using Robust.Shared.Map;
using Content.Server._KS14.NPC.Hands;

namespace Content.Server._KS14.NPC.Grenades;

/// <summary>
///     On an NPC that is holding a lit grenade before it throws it ("cooking" it, see
///         <see cref="NpcGrenadeThrowerComponent.Cooks"/>), for as long as it does. Everything that makes the throw safe
///         is in <see cref="NpcGrenadeSystem"/>: whatever ends the hold, the grenade leaves the hand first.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcGrenadeSystem))]
public sealed partial class NpcGrenadeCookComponent : Component
{
    [ViewVariables]
    public EntityUid GrenadeUid;

    /// <summary>
    ///     Where it is to be thrown. Belongs to its grid.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates Coordinates;

    /// <summary>
    ///     When it is thrown, at the latest: the fuse less the time in the air less the margin, so a throw that is not cut
    ///         short is made with that much fuse still to go.
    /// </summary>
    [ViewVariables]
    public TimeSpan ReleaseAt;

    /// <summary>
    ///     How long the task that is cooking can go without saying so before the hold is ended: a task that is no longer
    ///         running (a death, a sleeping NPC, a plan dropped without a word) cannot be trusted to end it.
    /// </summary>
    [ViewVariables]
    public TimeSpan LeaseUntil;

    /// <summary>
    ///     What was in hand before, to put back once it is thrown.
    /// </summary>
    public NpcTakenOut TakenOut;
}
