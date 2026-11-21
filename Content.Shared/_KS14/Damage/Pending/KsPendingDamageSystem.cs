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
///         It is kept beside the entity's damage, never in it: <see cref="DamageableComponent"/> holds what the server
///             dealt and nothing else, so no state has anything to put back. <see cref="DamageableSystem"/> adds it
///             to whatever asks for the entity's damage (<see cref="TryGetShownPending"/>), so a health bar, an
///             examine or anything else shows it with no code of its own. Not while a state is being applied: what
///             handles the server's damage then (thresholds, mob states) reads the server's alone, and does not act
///             on a hit the server may not make. Nothing is raised for it either: nothing bleeds, falls or dies for
///             pending damage.
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
    ///     What <see cref="DamageableSystem"/> adds to the entity's damage when asked for it
    ///         (<see cref="DamageableSystem.GetTotalDamage"/> and the getters beside it): all that is pending for it on
    ///         this client, by type.
    /// </summary>
    /// <returns>
    ///     False when there is nothing to add: nothing is pending, this is the server, or a state is being applied.
    ///         While one is, an entity's damage is the server's alone, so what handles the server's damage (thresholds,
    ///         damage visuals) reads what the server dealt and no more.
    /// </returns>
    public bool TryGetShownPending(EntityUid uid, out DamageSpecifier pending)
    {
        pending = default!;

        if (!_pendingDamageQuery.TryComp(uid, out var pendingComponent) ||
            pendingComponent.Entries.Count == 0 ||
            _gameTiming.ApplyingState)
            return false;

        pending = new DamageSpecifier();
        foreach (var entry in pendingComponent.Entries)
        {
            pending += entry.Damage;
        }

        return true;
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
