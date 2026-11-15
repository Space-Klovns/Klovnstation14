using Robust.Shared.Network;

namespace Content.Server._KS14.KillCredit;

/// <summary>
///     Whoever answers for what this entity does: a turret's kills are credited to the player who set it up, and a
///         bullet's to whoever its shooter answered to as it was fired. The player, not their body, so the credit
///         survives them dying and coming back in another.
/// </summary>
[RegisterComponent, Access(typeof(KsKillCreditSystem))]
public sealed partial class KsCreditOwnerComponent : Component
{
    [ViewVariables]
    public NetUserId? OwnerUserId;
}
