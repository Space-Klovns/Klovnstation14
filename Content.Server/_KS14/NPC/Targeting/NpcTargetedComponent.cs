using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Targeting;

/// <summary>
///     Who is fighting this entity, as of <see cref="NpcTargetTrackingSystem"/>'s last count: NPCs shooting or hitting
///         it, and how many of each faction. Only on an entity somebody is fighting; it goes once nobody is.
/// </summary>
[RegisterComponent, Access(typeof(NpcTargetTrackingSystem))]
public sealed partial class NpcTargetedComponent : Component
{
    /// <summary>
    ///     How many NPCs of each faction are fighting it. One of several factions counts for each.
    /// </summary>
    [ViewVariables]
    public Dictionary<ProtoId<NpcFactionPrototype>, int> Engaging = new();

    /// <summary>
    ///     The NPCs counted in <see cref="Engaging"/>, so that one asking about itself can be left out.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> Engagers = new();
}
