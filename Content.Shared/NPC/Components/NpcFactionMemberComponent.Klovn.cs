using Content.Shared._KS14.FactionCollision;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.NPC.Components;

public sealed partial class NpcFactionMemberComponent
{
    /// <summary>
    ///     On a client: whether the server has this client's own player's shots pass through this entity, as of the
    ///         last state (<see cref="KsFactionCollisionSystem"/>).
    /// </summary>
    /// <remarks>
    ///     <see cref="Factions"/> is not sent to clients, where it is whatever the entity's prototype has: sent, it
    ///         would tell a client who is on a side that is meant to be hidden. Each player is sent this answer about
    ///         themselves instead, which is no more than shooting at the entity would tell them.
    /// </remarks>
    [ViewVariables, Access(typeof(KsFactionCollisionSystem))]
    public bool KsLocalPlayerShotsPass;
}

/// <summary>
///     A faction member as one player is sent it: <paramref name="shotsPass"/> is about that player.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsNpcFactionMemberComponentState(bool shotsPass) : ComponentState
{
    public bool ShotsPass = shotsPass;
}
