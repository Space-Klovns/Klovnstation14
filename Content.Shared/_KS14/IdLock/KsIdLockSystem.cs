using Content.Shared.Access.Systems;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Popups;

namespace Content.Shared._KS14.IdLock;

public sealed class KsIdLockSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;

    [Dependency] private readonly EntityQuery<KsIdLockKeyComponent> _keyQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsIdLockComponent, LockToggleAttemptEvent>(OnLockToggleAttempt);

        SubscribeLocalEvent<KsIdLockComponent, ComponentShutdown>(OnLockShutdown);
        SubscribeLocalEvent<KsIdLockComponent, InteractUsingEvent>(OnLockInteractUsing);

        SubscribeLocalEvent<KsIdLockKeyComponent, ComponentShutdown>(OnKeyShutdown);
        SubscribeLocalEvent<KsIdLockKeyComponent, InteractUsingEvent>(OnKeyInteractUsing);
    }

    private void AddKeyToLock(Entity<KsIdLockComponent> lockEntity, Entity<KsIdLockKeyComponent> keyEntity)
    {
        lockEntity.Comp.AllowedUids.Add(keyEntity);
        Dirty(lockEntity);

        keyEntity.Comp.AttachedUids.Add(lockEntity);
        Dirty(keyEntity);
    }

    private void OnLockToggleAttempt(Entity<KsIdLockComponent> entity, ref LockToggleAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (entity.Comp.AllowedUids.Count == 0)
            goto cancel;

        var accessUids = _accessReaderSystem.FindPotentialAccessItems(args.User);
        if (accessUids.Count == 0)
            goto cancel;

        foreach (var accessUid in accessUids)
        {
            if (!entity.Comp.AllowedUids.Contains(accessUid))
                continue;

            // Found something that has access; don't cancel this
            return;
        }

        // Nothing found
    cancel:
        if (!args.Silent &&
            entity.Comp.ToggleLockDeniedPopupLoc is { } toggleLockDeniedPopupLoc)
            _popupSystem.PopupClient(Loc.GetString(toggleLockDeniedPopupLoc), entity, args.User);

        args.Cancelled = true;
        return;
    }

    private void OnLockShutdown(Entity<KsIdLockComponent> entity, ref ComponentShutdown args)
    {
        // Forget about freeman
        foreach (var keyUid in entity.Comp.AllowedUids)
            Comp<KsIdLockKeyComponent>(keyUid).AttachedUids.Remove(entity);
    }

    private void OnLockInteractUsing(Entity<KsIdLockComponent> entity, ref InteractUsingEvent args)
    {
        // Try to claim this console

        if (!entity.Comp.AllowClaiming)
            return;

        var accessUids = _accessReaderSystem.FindPotentialAccessItems(args.Used);
        if (accessUids.Count == 0)
            return;

        Entity<KsIdLockKeyComponent>? keyEntity = null;
        foreach (var accessUid in accessUids)
        {
            if (!_keyQuery.TryGetComponent(accessUid, out var keyComponent))
                continue;

            keyEntity = (accessUid, keyComponent);
            break;
        }

        if (keyEntity is not { })
            return;

        entity.Comp.AllowClaiming = false;
        entity.Comp.AllowedUids.Add(keyEntity.Value);
        Dirty(entity);

        keyEntity.Value.Comp.AttachedUids.Add(entity);
        Dirty(keyEntity.Value);

        args.Handled = true;

        if (entity.Comp.ClaimPopupLoc is { } claimPopupLoc)
            _popupSystem.PopupClient(Loc.GetString(claimPopupLoc), args.Target, args.User);
    }

    private void OnKeyShutdown(Entity<KsIdLockKeyComponent> entity, ref ComponentShutdown args)
    {
        foreach (var lockUid in entity.Comp.AttachedUids)
            Comp<KsIdLockComponent>(lockUid).AllowedUids.Remove(entity);
    }

    private void OnKeyInteractUsing(Entity<KsIdLockKeyComponent> entity, ref InteractUsingEvent args)
    {
        if (!entity.Comp.Inheritable ||
            !_keyQuery.TryGetComponent(args.Used, out var otherKeyComponent))
            return;

        var originalAccessCount = otherKeyComponent.AttachedUids.Count;
        foreach (var lockUid in entity.Comp.AttachedUids)
        {
            if (otherKeyComponent.AttachedUids.Contains(lockUid))
                continue;

            AddKeyToLock((lockUid, Comp<KsIdLockComponent>(lockUid)), (args.Used, otherKeyComponent));
        }

        var inheritedCount = otherKeyComponent.AttachedUids.Count - originalAccessCount;
        if (inheritedCount == 0 &&
            entity.Comp.InheritPopupLoc is { } inheritPopupLoc)
            _popupSystem.PopupClient(Loc.GetString(inheritPopupLoc, ("count", inheritedCount)), args.User, args.User);
    }
}
