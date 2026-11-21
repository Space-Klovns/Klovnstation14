using Content.Shared.Damage;
using Content.Shared.Damage.Systems;

namespace Content.Shared._KS14.Damage.Pending;

/// <summary>
///     On a client only: damage this client has predicted for the entity that only the server deals, shown on top of the
///         server's until the server's arrives. The hit of a client's kept copy of its own bullet, which is never
///         re-predicted: dealt as ordinary damage it was put back by the next state and dealt again by the server's
///         hit, and health flickered. See <see cref="DamageableSystem.KsAddPendingDamage"/>.
/// </summary>
/// <remarks>
///     Not networked and not saved: it is added by the client that predicted the hit, and gone once nothing is pending.
/// </remarks>
[RegisterComponent, Access(typeof(DamageableSystem))]
public sealed partial class KsPendingDamageComponent : Component
{
    /// <summary>
    ///     What is pending, oldest first.
    /// </summary>
    [ViewVariables]
    public List<KsPendingDamage> Entries = new();

    /// <summary>
    ///     The entity's damage in the last state the server sent, without anything pending: what a newer state is
    ///         compared with, to tell that the server has dealt what was pending.
    /// </summary>
    [ViewVariables]
    public DamageSpecifier ServerDamage = new();
}

/// <summary>
///     One predicted hit's damage.
/// </summary>
/// <param name="Damage">What it would deal, by type: resistances applied, all of it harm.</param>
/// <param name="ExpiresAt">The time after which the server is taken never to have dealt it.</param>
public readonly record struct KsPendingDamage(DamageSpecifier Damage, TimeSpan ExpiresAt);
