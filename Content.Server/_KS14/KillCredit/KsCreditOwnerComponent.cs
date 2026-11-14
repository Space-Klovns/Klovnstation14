namespace Content.Server._KS14.KillCredit;

/// <summary>
///     Whoever answers for what this entity does: a turret's kills are credited to the player who set it up.
/// </summary>
[RegisterComponent, Access(typeof(KsKillCreditSystem))]
public sealed partial class KsCreditOwnerComponent : Component
{
    [DataField]
    public EntityUid? OwnerUid;
}
