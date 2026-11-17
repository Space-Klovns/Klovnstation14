using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Throwing;
using Content.Shared.Whitelist;

namespace Content.Shared._KS14.Storage;

/// <summary>
///     Shuts an open <see cref="KsEntityStorageCatchThrownComponent"/> storage on what is thrown into it.
///     Shared, so whoever throws sees it happen at once: their client predicts the flight of what they throw
///         (<c>PredictedThrownItemComponent</c>), and so the hit.
/// </summary>
public sealed partial class KsEntityStorageCatchThrownSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorageSystem = default!;

    /// <summary>
    ///     An open storage keeps a fixture that thrown things hit, though nobody walking is stopped by it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnThrowHitBy(Entity<KsEntityStorageCatchThrownComponent> storage, ref ThrowHitByEvent args)
    {
        var thrown = args.Thrown;
        var thrower = args.Component.Thrower;
        if (!TryComp<EntityStorageComponent>(storage, out var entityStorage) || !entityStorage.Open ||
            !_whitelistSystem.IsWhitelistPassOrNull(storage.Comp.Whitelist, thrown) ||
            !_entityStorageSystem.CanClose(storage, thrower))
        {
            return;
        }

        // Closing takes in what lies in the opening, which someone still in flight at its edge doesn't yet. An open
        // storage also accepts anything, so what really fits can only be asked once it is shut.
        // The thrower is the user, so their client plays the door at once and the server leaves them out of its own.
        _entityStorageSystem.CloseStorage(storage, entityStorage, thrower);
        if (!entityStorage.Contents.Contains(thrown) &&
            (!_entityStorageSystem.CanInsert(thrown, storage, entityStorage) ||
             !_entityStorageSystem.Insert(thrown, storage, entityStorage)))
        {
            return;
        }

        _adminLogger.Add(LogType.Landed, LogImpact.Low,
            $"{ToPrettyString(thrown):thrown} thrown by {ToPrettyString(thrower):player} landed in {ToPrettyString(storage):storage}, which shut on them");
    }
}
