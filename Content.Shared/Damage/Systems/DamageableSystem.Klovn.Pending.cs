using Content.Shared._KS14.Damage.Pending;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Timing;

namespace Content.Shared.Damage.Systems;

/// <summary>
///     Pending damage (<see cref="KsPendingDamageComponent"/>): what a client shows of a hit it predicted and only the
///         server deals.
/// </summary>
/// <remarks>
///     <para>
///         What a client shows as an entity's damage is the server's plus whatever is pending for it. Pending damage is
///             written into <see cref="DamageableComponent.Damage"/> quietly: the totals are kept up, and nothing
///             else happens. No <see cref="DamageChangedEvent"/> (nothing bleeds, falls or dies for it: the server's
///             damage does that), and the component is not dirtied, so the state reset leaves it alone.
///     </para>
///     <para>
///         Every state of the component, a reset's included, is the server's damage, and what is still pending is put
///             back on top of it (<see cref="KsAddPendingToState"/>). A state in which the server's damage has gone
///             up by about what an entry holds, in that entry's types, is the server's hit for it: the entry goes,
///             and the entity's damage does not move. One the server never deals expires, and is taken back off, once.
///     </para>
/// </remarks>
public sealed partial class DamageableSystem
{
    [Dependency] private IGameTiming _ksGameTiming = default!;
    [Dependency] private Robust.Shared.Player.ISharedPlayerManager _ksPlayerManager = default!;
    [Dependency] private EntityQuery<KsPendingDamageComponent> _ksPendingDamageQuery = default!;

    /// <summary>
    ///     How long pending damage waits for the server's, over the client's round trip: the client's lead on the
    ///         server and the states it holds back before applying them.
    /// </summary>
    private static readonly TimeSpan KsPendingDamageMargin = TimeSpan.FromSeconds(0.5);

    /// <summary>
    ///     The longest pending damage waits, whatever the ping.
    /// </summary>
    private static readonly TimeSpan KsPendingDamageMaxLifetime = TimeSpan.FromSeconds(1.5);

    /// <summary>
    ///     The share of an entry the server's damage has to have gone up by, in the entry's own types, to be the hit
    ///         it was pending for. Less than all of it: the server's hit may be a little weaker (a shield, falloff from
    ///         a slightly different place), and anything unrelated is rarely in the same types and near the same size.
    /// </summary>
    private const float KsPendingDamageMatchShare = 0.5f;

    /// <summary>
    ///     On a client, shows <paramref name="damage"/> on <paramref name="ent"/> at once, as damage that is pending
    ///         until the server deals it. Does nothing on the server.
    /// </summary>
    /// <param name="damage">What the hit would deal, resistances applied (<see cref="PreviewDamage"/>).</param>
    public void KsAddPendingDamage(Entity<DamageableComponent?> ent, DamageSpecifier damage)
    {
        if (!_netMan.IsClient ||
            !_damageableQuery.Resolve(ent, ref ent.Comp, false) ||
            !_injurableQuery.TryComp(ent, out var injurableComponent))
            return;

        // Harm only, in the types the entity takes: what the server's hit will add.
        var pendingDamage = new DamageSpecifier();
        foreach (var (type, amount) in damage.DamageDict)
        {
            if (amount > FixedPoint2.Zero && SupportsType(injurableComponent.DamageContainer, type))
                pendingDamage.DamageDict[type] = amount;
        }

        if (pendingDamage.Empty)
            return;

        var pendingComponent = EnsureComp<KsPendingDamageComponent>(ent);
        if (pendingComponent.Entries.Count == 0)
            pendingComponent.ServerDamage = ent.Comp.Damage.Clone();

        var ping = TimeSpan.FromMilliseconds(_ksPlayerManager.LocalSession?.Channel.Ping ?? 0);
        var lifetime = ping + KsPendingDamageMargin;
        if (lifetime > KsPendingDamageMaxLifetime)
            lifetime = KsPendingDamageMaxLifetime;

        pendingComponent.Entries.Add(new KsPendingDamage(pendingDamage, _ksGameTiming.CurTime + lifetime));
        KsShiftDamageQuietly((ent, ent.Comp), pendingDamage, add: true);
    }

    /// <summary>
    ///     The damage pending for <paramref name="uid"/> on this client, in all: zero on the server.
    /// </summary>
    public FixedPoint2 KsGetPendingDamage(EntityUid uid)
    {
        var total = FixedPoint2.Zero;
        if (!_ksPendingDamageQuery.TryComp(uid, out var pendingComponent))
            return total;

        foreach (var entry in pendingComponent.Entries)
        {
            total += entry.Damage.GetTotal();
        }

        return total;
    }

    /// <summary>
    ///     Adds <paramref name="shift"/> to the entity's damage, or takes it off, and keeps the totals up. Nothing else:
    ///         see the class remarks.
    /// </summary>
    private void KsShiftDamageQuietly(Entity<DamageableComponent> ent, DamageSpecifier shift, bool add)
    {
        var dict = ent.Comp.Damage.DamageDict;
        foreach (var (type, amount) in shift.DamageDict)
        {
            var newValue = FixedPoint2.Max(FixedPoint2.Zero, dict.GetValueOrDefault(type) + (add ? amount : -amount));
            if (newValue == FixedPoint2.Zero)
                dict.Remove(type);
            else
                dict[type] = newValue;
        }

        ent.Comp.Damage.GetDamagePerGroup(ProtoMan, ent.Comp.DamagePerGroup);
        ent.Comp.TotalDamage = ent.Comp.Damage.GetTotal();
    }

    /// <summary>
    ///     For a state of the component: drops what was pending for damage the server has now dealt, and puts the rest
    ///         on top of the state's damage.
    /// </summary>
    /// <param name="serverDamage">The state's damage.</param>
    /// <param name="shownDamage">A copy of it, to add what is still pending to: what the component is about to hold.</param>
    private void KsAddPendingToState(EntityUid uid, DamageSpecifier serverDamage, DamageSpecifier shownDamage)
    {
        if (!_ksPendingDamageQuery.TryComp(uid, out var pendingComponent) || pendingComponent.Entries.Count == 0)
            return;

        // What the server's damage went up by since the last state, by type. A reset hands the same state again: nothing.
        var increase = serverDamage - pendingComponent.ServerDamage;
        pendingComponent.ServerDamage = serverDamage.Clone();

        var entries = pendingComponent.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            var entryDamage = entries[i].Damage;

            var covered = FixedPoint2.Zero;
            foreach (var (type, amount) in entryDamage.DamageDict)
            {
                covered += FixedPoint2.Min(amount, FixedPoint2.Max(FixedPoint2.Zero, increase.DamageDict.GetValueOrDefault(type)));
            }

            if (covered <= FixedPoint2.Zero || (float)covered < (float)entryDamage.GetTotal() * KsPendingDamageMatchShare)
                continue;

            // The server's hit for this one: its share of the increase is spent, and it is no longer pending.
            foreach (var (type, amount) in entryDamage.DamageDict)
            {
                if (increase.DamageDict.TryGetValue(type, out var left))
                    increase.DamageDict[type] = left - FixedPoint2.Min(amount, FixedPoint2.Max(FixedPoint2.Zero, left));
            }

            entries.RemoveAt(i);
            i--;
        }

        foreach (var entry in entries)
        {
            foreach (var (type, amount) in entry.Damage.DamageDict)
            {
                shownDamage.DamageDict[type] = shownDamage.DamageDict.GetValueOrDefault(type) + amount;
            }
        }

        if (entries.Count == 0)
            RemCompDeferred<KsPendingDamageComponent>(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Pending damage the server never dealt is taken back off, once. Only clients have any.
        if (!_netMan.IsClient || !_ksGameTiming.IsFirstTimePredicted)
            return;

        var now = _ksGameTiming.CurTime;
        var query = EntityQueryEnumerator<KsPendingDamageComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var pendingComponent, out var damageableComponent))
        {
            var entries = pendingComponent.Entries;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].ExpiresAt > now)
                    continue;

                KsShiftDamageQuietly((uid, damageableComponent), entries[i].Damage, add: false);
                entries.RemoveAt(i);
            }

            if (entries.Count == 0)
                RemCompDeferred<KsPendingDamageComponent>(uid);
        }
    }
}
