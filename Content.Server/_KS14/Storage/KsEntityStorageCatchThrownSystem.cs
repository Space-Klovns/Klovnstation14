using Content.Server.Administration.Logs;
using Content.Shared._KS14.Storage;
using Content.Shared.Database;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Throwing;
using Content.Shared.Whitelist;

namespace Content.Server._KS14.Storage;

/// <summary>
///     Shuts an open <see cref="KsEntityStorageCatchThrownComponent"/> storage on what is thrown into it.
///     Server only, as <c>ThrowInsertContainerSystem</c> is: a client can't predict where someone else's throw lands.
/// </summary>
public sealed partial class KsEntityStorageCatchThrownSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorageSystem = default!;

    /// <summary>
    ///     An open storage keeps a fixture that thrown things hit, though nobody walking is stopped by it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnThrowHitBy(Entity<KsEntityStorageCatchThrownComponent> storage, ref ThrowHitByEvent args)
    {
        var thrown = args.Thrown;
        if (!TryComp<EntityStorageComponent>(storage, out var entityStorage) || !entityStorage.Open ||
            !_whitelistSystem.IsWhitelistPassOrNull(storage.Comp.Whitelist, thrown) ||
            !_entityStorageSystem.CanClose(storage))
        {
            return;
        }

        // Closing takes in what lies in the opening, which someone still in flight at its edge doesn't yet. An open
        // storage also accepts anything, so what really fits can only be asked once it is shut.
        _entityStorageSystem.CloseStorage(storage, entityStorage);
        if (!entityStorage.Contents.Contains(thrown) &&
            (!_entityStorageSystem.CanInsert(thrown, storage, entityStorage) ||
             !_entityStorageSystem.Insert(thrown, storage, entityStorage)))
        {
            return;
        }

        _adminLogger.Add(LogType.Landed, LogImpact.Low,
            $"{ToPrettyString(thrown):thrown} thrown by {ToPrettyString(args.Component.Thrower):player} landed in {ToPrettyString(storage):storage}, which shut on them");
    }
}
