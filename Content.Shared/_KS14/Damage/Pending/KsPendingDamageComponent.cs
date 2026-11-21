using Content.Shared.Damage;

namespace Content.Shared._KS14.Damage.Pending;

/// <summary>
///     On a client only: damage this client has predicted for the entity that only the server deals, shown on top of the
///         server's until the server deals it. The hit of a client's kept copy of its own bullet, which is never
///         re-predicted: dealt as ordinary damage it was put back by the next state and dealt again by the server's
///         hit, and health flickered. See <see cref="KsPendingDamageSystem"/>.
/// </summary>
/// <remarks>
///     Not networked and not saved: it is added by the client that predicted the hit, and gone once nothing is pending.
/// </remarks>
[RegisterComponent, Access(typeof(KsPendingDamageSystem))]
public sealed partial class KsPendingDamageComponent : Component
{
    /// <summary>
    ///     What is pending, oldest first.
    /// </summary>
    [ViewVariables]
    public List<KsPendingDamage> Entries = new();
}

/// <summary>
///     One predicted hit's damage.
/// </summary>
/// <param name="Damage">What it would deal, by type: resistances applied, all of it harm.</param>
/// <param name="ShotId">This client's id for the shot whose hit it is: what the server's hit of that shot is matched by.</param>
/// <param name="ExpiresAt">The time after which the server is taken never to have dealt it.</param>
public readonly record struct KsPendingDamage(DamageSpecifier Damage, int ShotId, TimeSpan ExpiresAt);
