// KS14: added in this fork
using System.Collections.Generic;
using Content.Shared.Storage.Components;
using Robust.Shared.Network; // KS14

namespace Content.Shared.Storage.EntitySystems;

public abstract partial class SharedStorageSystem
{
    [Dependency] private INetManager _netManager = default!; // KS14: only the server evicts storage windows

    // KS14: preserves the order in which each actor opened storage windows.
    private readonly Dictionary<EntityUid, LinkedList<EntityUid>> _openStorageWindows = [];

    private void AddOpenStorageWindow(EntityUid actor, EntityUid storage)
    {
        var windows = _openStorageWindows.GetValueOrDefault(actor) ?? [];
        windows.Remove(storage);
        windows.AddLast(storage);
        _openStorageWindows[actor] = windows;
    }

    private void RemoveOpenStorageWindow(EntityUid actor, EntityUid storage)
    {
        if (!_openStorageWindows.TryGetValue(actor, out var windows))
            return;

        windows.Remove(storage);
        if (windows.Count == 0)
            _openStorageWindows.Remove(actor);
    }

    private bool IsStorageWindowLimitReached(EntityUid actor)
    {
        return _openStorageLimit == 0 ||
               _openStorageLimit > 0 &&
               _openStorageWindows.TryGetValue(actor, out var windows) &&
               windows.Count >= _openStorageLimit;
    }

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
