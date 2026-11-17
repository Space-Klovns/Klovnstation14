using Content.Client.Research.UI;
using Content.Shared._KS14.Research.Components;
using Content.Shared.Research.Components;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.Research;

/// <summary>
///     Refreshes open research server selection windows when the server list or the selection changes.
/// </summary>
public sealed partial class KsResearchServerListUiSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;

    [SubscribeLocalEvent]
    private void OnServerListState(Entity<KsResearchServerListComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        var clientQuery = EntityQueryEnumerator<ResearchClientComponent, TransformComponent>();
        while (clientQuery.MoveNext(out var clientUid, out _, out var transformComponent))
        {
            if (transformComponent.MapUid == entity.Owner)
                RefreshWindow(clientUid);
        }
    }

    [SubscribeLocalEvent]
    private void OnResearchClientState(Entity<ResearchClientComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        RefreshWindow(entity.Owner);
    }

    private void RefreshWindow(EntityUid clientUid)
    {
        if (_userInterfaceSystem.TryGetOpenUi<ResearchClientBoundUserInterface>(clientUid, ResearchClientUiKey.Key, out var boundUserInterface))
            boundUserInterface.Update();
    }
}
