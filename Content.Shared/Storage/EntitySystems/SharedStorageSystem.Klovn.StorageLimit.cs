// KS14: added in this fork
using System.Collections.Generic;
using Content.Shared.Storage.Components;
using Robust.Shared.Network;

namespace Content.Shared.Storage.EntitySystems;

/// <summary>
///     Storage window limiting by eviction: at the limit, the oldest window is closed to make room for
///         the new one, rather than the new one being refused.
/// </summary>
public abstract partial class SharedStorageSystem
{
    [Dependency] private INetManager _netManager = default!;

    /// <summary>
    ///     Storage windows each actor has open, oldest first.
    /// </summary>
    private readonly Dictionary<EntityUid, LinkedList<EntityUid>> _openStorageWindows = [];

    /// <summary>
    ///     Drops an actor's tracked windows when the actor goes away.
    /// </summary>
    /// <remarks>
    ///     Without this the dictionary grows for the lifetime of the process, and - because entity uids
    ///         are recycled - a new entity reusing an old uid would inherit a stale list and be refused
    ///         windows it never opened. Round restart is covered too, since everything terminates then.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnStorageWindowUserTerminating(Entity<UserInterfaceUserComponent> entity, ref EntityTerminatingEvent args)
    {
        _openStorageWindows.Remove(entity.Owner);
    }

    /// <summary>
    ///     Records that an actor opened a storage window, as the most recent one.
    /// </summary>
    private void AddOpenStorageWindow(EntityUid actor, EntityUid storage)
    {
        var windows = _openStorageWindows.GetValueOrDefault(actor) ?? [];
        windows.Remove(storage);
        windows.AddLast(storage);
        _openStorageWindows[actor] = windows;
    }

    /// <summary>
    ///     Forgets a storage window an actor no longer has open.
    /// </summary>
    private void RemoveOpenStorageWindow(EntityUid actor, EntityUid storage)
    {
        if (!_openStorageWindows.TryGetValue(actor, out var windows))
            return;

        windows.Remove(storage);
        if (windows.Count == 0)
            _openStorageWindows.Remove(actor);
    }

    /// <summary>
    ///     Closes as many of the actor's oldest storage windows as it takes to fit one more.
    /// </summary>
    /// <remarks>
    ///     On the client this only hides the outgoing window and hands its screen position to the
    ///         incoming one, so the replacement opens where the old one was. The authoritative close
    ///         comes from the server a moment later; until it arrives the client's list is deliberately
    ///         one short, which is the prediction.
    /// </remarks>
    /// <returns>False if the actor may not open a storage window at all.</returns>
    private bool MakeRoomForStorageWindow(EntityUid storage, EntityUid actor)
    {
        if (_openStorageLimit < 0 || UI.IsUiOpen(storage, StorageComponent.StorageUiKey.Key, actor))
            return true;

        if (_openStorageLimit == 0)
            return false;

        if (!_openStorageWindows.TryGetValue(actor, out var windows))
            return true;

        while (windows.Count >= _openStorageLimit)
        {
            var oldestStorage = windows.First!.Value;

            if (_netManager.IsClient)
            {
                PrepareStorageWindowReplacement(oldestStorage, storage, actor);
                windows.RemoveFirst();
                continue;
            }

            UI.CloseUi(oldestStorage, StorageComponent.StorageUiKey.Key, actor);

            // Closing removes the entry through OnBoundUIClosed. Remove stale entries too.
            if (windows.First?.Value == oldestStorage)
                windows.RemoveFirst();
        }

        return true;
    }
}
