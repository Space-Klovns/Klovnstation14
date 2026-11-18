using Content.Shared._KS14.ComplexShove;
using Content.Shared._KS14.KsPopup;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Throwing;
using Content.Shared.Whitelist;

namespace Content.Shared._KS14.Storage;

/// <summary>
///     Shuts an open <see cref="KsEntityStorageCatchComponent"/> storage on whoever is thrown or shoved into it.
///     Shared, so whoever does it sees it happen at once: a shove is predicted, and so is the flight of what a
///         client throws (<c>PredictedThrownItemComponent</c>), and with it the hit.
/// </summary>
public sealed partial class KsEntityStorageCatchSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private KsPopupSystem _ksPopupSystem = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorageSystem = default!;

    /// <summary>
    ///     An open storage keeps a fixture that thrown things hit, though nobody walking is stopped by it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnThrowHitBy(Entity<KsEntityStorageCatchComponent> storage, ref ThrowHitByEvent args)
    {
        TryCatch(storage, args.Thrown, args.Component.Thrower);
    }

    /// <summary>
    ///     Someone shoved into the storage goes in rather than being knocked down against it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnShoveCollide(Entity<KsEntityStorageCatchComponent> storage, ref KsShoveCollideEvent args)
    {
        if (!args.Handled && TryCatch(storage, args.Target, args.Shover))
            args.Handled = true;
    }

    /// <summary>
    ///     Puts <paramref name="caught"/> into the storage and shuts it, if it stands open and takes them.
    /// </summary>
    /// <param name="user">Who sent them in, if anyone: they hear the door at once, and are named in the popups.</param>
    public bool TryCatch(Entity<KsEntityStorageCatchComponent> storage, EntityUid caught, EntityUid? user)
    {
        if (!TryComp<EntityStorageComponent>(storage, out var entityStorage) || !entityStorage.Open ||
            !_whitelistSystem.IsWhitelistPassOrNull(storage.Comp.Whitelist, caught) ||
            !_entityStorageSystem.CanClose(storage, user))
        {
            return false;
        }

        // Closing takes in what lies in the opening, which someone at its edge doesn't yet. An open storage also
        // accepts anything, so what really fits can only be asked once it is shut.
        _entityStorageSystem.CloseStorage(storage, entityStorage, user);
        if (!entityStorage.Contents.Contains(caught) &&
            (!_entityStorageSystem.CanInsert(caught, storage, entityStorage) ||
             !_entityStorageSystem.Insert(caught, storage, entityStorage)))
        {
            return false;
        }

        if (user is { } userUid)
        {
            _ksPopupSystem.PopupUserTargetOthers(storage, userUid, caught, storage.Comp.Popups, PopupType.MediumCaution,
                ("user", Identity.Entity(userUid, EntityManager)),
                ("target", Identity.Entity(caught, EntityManager)),
                ("storage", storage.Owner));
        }

        if (user is { } senderUid)
        {
            _adminLogger.Add(LogType.Landed, LogImpact.Low,
                $"{ToPrettyString(caught):target} was sent by {ToPrettyString(senderUid):player} into {ToPrettyString(storage):storage}, which shut on them");
        }
        else
        {
            _adminLogger.Add(LogType.Landed, LogImpact.Low,
                $"{ToPrettyString(caught):target} was sent by nobody into {ToPrettyString(storage):storage}, which shut on them");
        }
        return true;
    }
}
