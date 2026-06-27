using Content.Shared.NPC.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.FactionCollision;

/// <summary>
///     Component that denotes which faction(s) members of will
///         not collide with this entity.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
[Access(typeof(KsFactionCollisionSystem))]
public sealed partial class KsFactionCollisionComponent : Component
{
    /// <summary>
    ///     List of faction IDs that members of will not
    ///         collide with this entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public HashSet<ProtoId<NpcFactionPrototype>> Factions = [];
}
