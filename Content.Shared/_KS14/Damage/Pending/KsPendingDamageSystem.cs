using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Damage.Pending;

/// <summary>
///     Pending damage (<see cref="KsPendingDamageComponent"/>): what a client shows of a hit it predicted and only the
///         server deals, so the shooter sees their hit land at once and not a round trip later.
/// </summary>
/// <remarks>
///     <para>
///         It is kept beside the entity's damage, never in it. <see cref="DamageableComponent"/> holds what the server
///             dealt and nothing else, so nothing that reads it (thresholds, mob states, bleeding) acts on a hit the
///             server may not make, and no state has anything to put back. What shows a health value adds it:
///             <see cref="GetShownTotalDamage"/>.
///     </para>
///     <para>
///         Each entry is one shot's hit (<see cref="KsPendingDamage.ShotId"/>). The server tells the shooter's client of
///             every hit of their shot (<c>KsPredictedSpawnImpactEvent</c>), and the engine hands that over once the
///             state of its tick is applied, before anything is drawn: that state has the server's damage in it, and
///             the entry goes then (<see cref="Resolve"/>), so the value shown does not move. A hit the server never
///             makes on that entity expires, and comes off once.
///     </para>
/// </remarks>
public sealed partial class KsPendingDamageSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;

    [Dependency] private EntityQuery<KsPendingDamageComponent> _pendingDamageQuery = default!;

    /// <summary>
    ///     How long pending damage waits for the server's hit, over the client's ping: the client's lead on the server,
    ///         and the states it holds back before applying them.
    /// </summary>
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(0.5);

    /// <summary>
    ///     The longest pending damage waits, whatever the ping.
    /// </summary>
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromSeconds(1.5);

    /// <summary>
    ///     On a client, shows <paramref name="damage"/> on <paramref name="uid"/> at once, as pending until the server's
    ///         hit of the shot <paramref name="shotId"/> deals it. Does nothing on the server.
    /// </summary>
    /// <param name="damage">What the hit would deal, resistances applied (<see cref="DamageableSystem.PreviewDamage"/>).</param>
    public void Add(EntityUid uid, DamageSpecifier damage, int shotId)
    {
        // Only what the server's hit would be written to.
        if (!_netManager.IsClient || !HasComp<DamageableComponent>(uid) || !HasComp<InjurableComponent>(uid))
            return;

        var pendingDamage = DamageSpecifier.GetPositive(damage);
        if (pendingDamage.Empty)
            return;

        var ping = TimeSpan.FromMilliseconds(_playerManager.LocalSession?.Channel.Ping ?? 0);
        var lifetime = ping + Margin;
        if (lifetime > MaxLifetime)
            lifetime = MaxLifetime;

        EnsureComp<KsPendingDamageComponent>(uid).Entries.Add(new KsPendingDamage(pendingDamage, shotId, _gameTiming.CurTime + lifetime));
    }

    /// <summary>
    ///     The server's hit of the shot <paramref name="shotId"/> on <paramref name="uid"/> has been dealt: what was
    ///         pending for it no longer is.
    /// </summary>
    /// <returns>Whether anything was pending for it.</returns>
    public bool Resolve(EntityUid uid, int shotId)
    {
        if (!_pendingDamageQuery.TryComp(uid, out var pendingComponent))
            return false;

        var entries = pendingComponent.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].ShotId != shotId)
                continue;

            entries.RemoveAt(i);
            if (entries.Count == 0)
                RemCompDeferred<KsPendingDamageComponent>(uid);

            return true;
        }

        return false;
    }

    /// <summary>
    ///     The damage pending for <paramref name="uid"/> on this client, in all. Zero on the server.
    /// </summary>
    public FixedPoint2 GetPendingDamage(EntityUid uid)
    {
        var total = FixedPoint2.Zero;
        if (!_pendingDamageQuery.TryComp(uid, out var pendingComponent))
            return total;

        foreach (var entry in pendingComponent.Entries)
        {
            total += entry.Damage.GetTotal();
        }

        return total;
    }

    /// <summary>
    ///     The entity's total damage as a player is shown it: the server's, and on a client whatever that client has
    ///         pending for it. For what draws a health value. Anything that decides something reads
    ///         <see cref="DamageableSystem.GetTotalDamage"/>.
    /// </summary>
    public FixedPoint2 GetShownTotalDamage(Entity<DamageableComponent?> entity)
    {
        return _damageableSystem.GetTotalDamage(entity) + GetPendingDamage(entity.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Pending damage the server never dealt comes off, once. Only clients have any.
        if (!_netManager.IsClient || !_gameTiming.IsFirstTimePredicted)
            return;

        var now = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsPendingDamageComponent>();
        while (query.MoveNext(out var uid, out var pendingComponent))
        {
            if (pendingComponent.LifeStage > ComponentLifeStage.Running)
                continue; // already on its way out, with nothing pending

            var entries = pendingComponent.Entries;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].ExpiresAt <= now)
                    entries.RemoveAt(i);
            }

            if (entries.Count == 0)
                RemCompDeferred<KsPendingDamageComponent>(uid);
        }
    }
}
