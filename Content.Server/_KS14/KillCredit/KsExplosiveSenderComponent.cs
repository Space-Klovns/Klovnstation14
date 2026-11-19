namespace Content.Server._KS14.KillCredit;

/// <summary>
///     Who sent this explosive on its way, for one that is not a projectile and whose trigger names nobody, or the wrong
///         one: a mortar shell, set off by landing. Its explosion is that sender's in the admin log, for kill credit and
///         for the Field Commander's damage ledger, as a rocket's is its shooter's (<c>ExplosionSystem.KsResolveUser</c>).
///     <para>
///         Set it with <see cref="KsKillCreditSystem.SetSender"/>, which also stamps the explosive with whoever the sender
///             answers to, so it still pays the right player once the sender is gone.
///     </para>
/// </summary>
[RegisterComponent, Access(typeof(KsKillCreditSystem))]
public sealed partial class KsExplosiveSenderComponent : Component
{
    /// <summary>
    ///     Whoever sent it. It may be deleted by the time it goes off.
    /// </summary>
    [ViewVariables]
    public EntityUid? SenderUid;
}
