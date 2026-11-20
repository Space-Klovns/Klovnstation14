using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusEffectNew;
using Content.Shared.StatusEffectNew.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Perks;

/// <summary>
///     Gives perks to bodies and takes them away. What a perk gives is recorded on the body
///         (<see cref="KsPerkHolderComponent.Grants"/>), so taking it away removes exactly that and leaves whatever the
///         body had before.
/// </summary>
/// <remarks>
///     Giving and taking are server-authoritative: on the client they return false and change nothing. Which perks a
///         body holds is networked, so <see cref="HasPerk"/> works on both sides. Ability systems react to their own
///         component being added and removed, or to <see cref="KsPerkAddedEvent"/> and <see cref="KsPerkRemovedEvent"/>.
/// </remarks>
public abstract partial class SharedKsPerkSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private AlertsSystem _alertsSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedMindSystem _mindSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private StatusEffectsSystem _statusEffectsSystem = default!;
    [Dependency] private EntityQuery<KsPerkHolderComponent> _holderQuery = default!;
    [Dependency] private EntityQuery<StatusEffectComponent> _statusEffectQuery = default!;

    /// <summary>
    ///     Whether the body holds the perk.
    /// </summary>
    public bool HasPerk(Entity<KsPerkHolderComponent?> entity, ProtoId<KsPerkPrototype> perk)
    {
        return _holderQuery.Resolve(entity.Owner, ref entity.Comp, logMissing: false) && entity.Comp.Perks.Contains(perk);
    }

    /// <summary>
    ///     Gives the body a perk and everything it grants. False if it already holds it, the perk doesn't exist, or
    ///         this is the client.
    /// </summary>
    public bool TryAddPerk(EntityUid bodyUid, ProtoId<KsPerkPrototype> perk)
    {
        if (_netManager.IsClient ||
            TerminatingOrDeleted(bodyUid) ||
            !ProtoMan.TryIndex(perk, out var perkPrototype))
            return false;

        var holderComponent = EnsureComp<KsPerkHolderComponent>(bodyUid);
        if (holderComponent.Perks.Contains(perk))
            return false;

        var grant = new KsPerkGrant();
        holderComponent.Perks.Add(perk);
        holderComponent.Grants[perk] = grant;
        Dirty(bodyUid, holderComponent);

        GrantComponents(bodyUid, perkPrototype, grant);
        GrantStatusEffects(bodyUid, perkPrototype, grant);
        GrantActions(bodyUid, perkPrototype, grant);
        GrantItems(bodyUid, perkPrototype, grant);

        if (perkPrototype.Alert is { } alert)
            _alertsSystem.ShowAlert(bodyUid, alert);

        var ev = new KsPerkAddedEvent(perk);
        RaiseLocalEvent(bodyUid, ref ev);
        return true;
    }

    /// <summary>
    ///     Takes a perk away and undoes exactly what it gave. False if the body doesn't hold it, or this is the client.
    /// </summary>
    public bool RemovePerk(Entity<KsPerkHolderComponent?> entity, ProtoId<KsPerkPrototype> perk)
    {
        if (_netManager.IsClient ||
            !_holderQuery.Resolve(entity.Owner, ref entity.Comp, logMissing: false) ||
            !entity.Comp.Perks.Remove(perk))
            return false;

        Dirty(entity);

        if (entity.Comp.Grants.Remove(perk, out var grant))
        {
            ProtoMan.TryIndex(perk, out var perkPrototype);
            RevokeItems(grant);
            RevokeActions(entity.Owner, grant);
            RevokeStatusEffects((entity.Owner, entity.Comp), grant);
            RevokeComponents((entity.Owner, entity.Comp), grant);

            if (perkPrototype?.Alert is { } alert)
                _alertsSystem.ClearAlert(entity.Owner, alert);
        }

        var ev = new KsPerkRemovedEvent(perk);
        RaiseLocalEvent(entity.Owner, ref ev);
        return true;
    }

    /// <summary>
    ///     Takes every perk away.
    /// </summary>
    public void RemoveAllPerks(Entity<KsPerkHolderComponent?> entity)
    {
        if (!_holderQuery.Resolve(entity.Owner, ref entity.Comp, logMissing: false))
            return;

        for (var i = entity.Comp.Perks.Count - 1; i >= 0; i--)
            RemovePerk(entity, entity.Comp.Perks[i]);
    }

    [SubscribeLocalEvent]
    private void OnHolderShutdown(Entity<KsPerkHolderComponent> entity, ref ComponentShutdown args)
    {
        // A holder removed from a living body takes its perks with it; a deleted body takes everything anyway.
        if (_netManager.IsClient || TerminatingOrDeleted(entity.Owner))
            return;

        RemoveAllPerks(entity.AsNullable());
    }

    #region Components

    private void GrantComponents(EntityUid bodyUid, KsPerkPrototype perkPrototype, KsPerkGrant grant)
    {
        foreach (var (name, entry) in perkPrototype.Components)
        {
            var registration = Factory.GetRegistration(name);
            if (HasComp(bodyUid, registration.Type))
                continue;

            EntityManager.AddComponent(bodyUid, entry);
            grant.Components.Add(name);
        }
    }

    private void RevokeComponents(Entity<KsPerkHolderComponent> entity, KsPerkGrant grant)
    {
        foreach (var name in grant.Components)
        {
            // Another perk still held wants it too: it carries on under that perk's grant.
            if (TryFindOtherGrant(entity, perkPrototype => perkPrototype.Components.ContainsKey(name), out var otherGrant))
            {
                otherGrant.Components.Add(name);
                continue;
            }

            RemComp(entity.Owner, Factory.GetRegistration(name).Type);
        }
    }

    #endregion

    #region Status effects

    private void GrantStatusEffects(EntityUid bodyUid, KsPerkPrototype perkPrototype, KsPerkGrant grant)
    {
        foreach (var effect in perkPrototype.StatusEffects)
        {
            if (_statusEffectsSystem.TryGetStatusEffect(bodyUid, effect, out var effectUid))
            {
                // Already permanent, and not this perk's doing: leave it be.
                if (!_statusEffectQuery.TryComp(effectUid, out var statusEffectComponent) ||
                    statusEffectComponent.EndEffectTime is not { } previousEndTime)
                    continue;

                if (_statusEffectsSystem.TrySetStatusEffectDuration(bodyUid, effect, duration: null))
                    grant.StatusEffects.Add(new KsPerkStatusEffectGrant(effect, Added: false, previousEndTime));

                continue;
            }

            if (_statusEffectsSystem.TrySetStatusEffectDuration(bodyUid, effect, duration: null))
                grant.StatusEffects.Add(new KsPerkStatusEffectGrant(effect, Added: true, PreviousEndTime: null));
        }
    }

    private void RevokeStatusEffects(Entity<KsPerkHolderComponent> entity, KsPerkGrant grant)
    {
        var curTime = _gameTiming.CurTime;
        foreach (var effectGrant in grant.StatusEffects)
        {
            if (TryFindOtherGrant(entity, perkPrototype => perkPrototype.StatusEffects.Contains(effectGrant.Effect), out var otherGrant))
            {
                otherGrant.StatusEffects.Add(effectGrant);
                continue;
            }

            // What was running before carries on as it would have; what the perk started stops.
            if (effectGrant.PreviousEndTime is { } endTime && endTime > curTime)
                _statusEffectsSystem.TrySetStatusEffectDuration(entity.Owner, effectGrant.Effect, endTime - curTime);
            else
                _statusEffectsSystem.TryRemoveStatusEffect(entity.Owner, effectGrant.Effect);
        }
    }

    #endregion

    #region Actions

    private void GrantActions(EntityUid bodyUid, KsPerkPrototype perkPrototype, KsPerkGrant grant)
    {
        foreach (var action in perkPrototype.Actions)
        {
            if (_actionsSystem.AddAction(bodyUid, action) is { } actionUid)
                grant.Actions.Add(actionUid);
        }
    }

    private void RevokeActions(EntityUid bodyUid, KsPerkGrant grant)
    {
        foreach (var actionUid in grant.Actions)
        {
            if (TerminatingOrDeleted(actionUid))
                continue;

            _actionsSystem.RemoveAction(bodyUid, actionUid);
            QueueDel(actionUid);
        }
    }

    #endregion

    #region Items

    private void GrantItems(EntityUid bodyUid, KsPerkPrototype perkPrototype, KsPerkGrant grant)
    {
        grant.ItemsWithdrawn = false;
        var coordinates = Transform(bodyUid).Coordinates;
        foreach (var item in perkPrototype.Items)
        {
            var itemUid = Spawn(item, coordinates);
            _handsSystem.PickupOrDrop(bodyUid, itemUid, checkActionBlocker: false, animate: false);
            grant.Items.Add(itemUid);
        }
    }

    private void RevokeItems(KsPerkGrant grant)
    {
        foreach (var itemUid in grant.Items)
        {
            if (!TerminatingOrDeleted(itemUid))
                QueueDel(itemUid);
        }

        grant.Items.Clear();
    }

    // The items a perk gives are for its holder while they use the body: they go when the holder dies or leaves the
    //      body for another, so a corpse or an abandoned body leaves no spare medigun for anyone, and the next body
    //      gets its own. They come back if the body is revived with its player in it, or its player returns to it.
    //      By-value events, so no Entity<T> handlers (RA0054).
    [SubscribeLocalEvent]
    private void OnHolderMobStateChanged(EntityUid uid, KsPerkHolderComponent component, MobStateChangedEvent args)
    {
        if (_netManager.IsClient)
            return;

        if (args.NewMobState == MobState.Dead)
            WithdrawItems((uid, component));
        else if (args.OldMobState == MobState.Dead && _mindSystem.TryGetMind(uid, out _, out _))
            ReturnItems((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnHolderMindRemoved(EntityUid uid, KsPerkHolderComponent component, MindRemovedMessage args)
    {
        if (!_netManager.IsClient)
            WithdrawItems((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnHolderMindAdded(EntityUid uid, KsPerkHolderComponent component, MindAddedMessage args)
    {
        if (!_netManager.IsClient && !_mobStateSystem.IsDead(uid))
            ReturnItems((uid, component));
    }

    private void WithdrawItems(Entity<KsPerkHolderComponent> entity)
    {
        foreach (var grant in entity.Comp.Grants.Values)
        {
            if (grant.ItemsWithdrawn)
                continue;

            RevokeItems(grant);
            grant.ItemsWithdrawn = true;
        }
    }

    /// <summary>
    ///     Gives back what <see cref="WithdrawItems"/> took; only that, so an item the holder lost some other way isn't
    ///         made again.
    /// </summary>
    private void ReturnItems(Entity<KsPerkHolderComponent> entity)
    {
        foreach (var (perk, grant) in entity.Comp.Grants)
        {
            if (grant.ItemsWithdrawn && ProtoMan.TryIndex(perk, out var perkPrototype))
                GrantItems(entity.Owner, perkPrototype, grant);
        }
    }

    #endregion

    /// <summary>
    ///     The grant of another perk the body holds whose prototype also lists something.
    /// </summary>
    private bool TryFindOtherGrant(Entity<KsPerkHolderComponent> entity, Func<KsPerkPrototype, bool> wants, out KsPerkGrant otherGrant)
    {
        foreach (var (otherPerk, grant) in entity.Comp.Grants)
        {
            if (ProtoMan.TryIndex(otherPerk, out var otherPrototype) && wants(otherPrototype))
            {
                otherGrant = grant;
                return true;
            }
        }

        otherGrant = default!;
        return false;
    }
}
