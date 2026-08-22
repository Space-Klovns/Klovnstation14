using Robust.Shared.Analyzers;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     Marks an NPC as having reserved a dynamically-picked tactical position (camping/retreat/advance
///     destination), so other NPCs querying TacticalPositionOperator avoid/discourage nearby candidates.
///     Added/refreshed by NpcTacticalPositionClaimSystem.Claim and removed on release or TTL expiry.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class NpcTacticalPositionClaimComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityCoordinates Coordinates;

    [AutoPausedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan ExpiresAt;

    [ViewVariables(VVAccess.ReadOnly)]
    public float ClearanceRadius;
}
