using Content.Shared._KS14.Research.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Research.Components;

namespace Content.Shared.Research.Systems;

public abstract partial class SharedResearchSystem
{
    [Dependency] private SharedUserInterfaceSystem _userInterfaceSystem = default!;
    [Dependency] private SharedPowerReceiverSystem _powerReceiverSystem = default!;

    /// <summary>
    ///     The research servers a client can connect to: those on its map. Works on client and server.
    /// </summary>
    public IReadOnlyList<KsResearchServerEntry> GetServerList(EntityUid clientUid)
    {
        if (EntityManager.TransformQuery.TryGetComponent(clientUid, out var transformComponent)
            && transformComponent.MapUid is { } mapUid
            && TryComp<KsResearchServerListComponent>(mapUid, out var listComponent))
        {
            return listComponent.Servers;
        }

        return [];
    }

    /// <summary>
    ///     Opens the server selection window from a console, lathe or the like. Shared so the client
    ///         opens it at once instead of waiting for the server.
    /// </summary>
    // A BoundUserInterfaceMessage is a by-value event, so this keeps the (uid, component, args) signature
    //      that the engine requires for them.
    [SubscribeLocalEvent]
    private void OnConsoleServerSelection(EntityUid uid, ResearchClientComponent component, ConsoleServerSelectionMessage args)
    {
        // A predicted message is raised again on each re-prediction, after the state reset has undone the
        //      toggle, so it toggles from the server's state every time and there is no first-time gate.
        if (!_powerReceiverSystem.IsPowered(uid))
            return;

        _userInterfaceSystem.TryToggleUi(uid, ResearchClientUiKey.Key, args.Actor);
    }
}
