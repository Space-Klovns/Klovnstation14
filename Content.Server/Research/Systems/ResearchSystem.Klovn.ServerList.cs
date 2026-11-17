using Content.Shared._KS14.Research.Components;
using Content.Shared.Research.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Utility;

namespace Content.Server.Research.Systems;

// Keeps KsResearchServerListComponent, on each map entity, listing the research servers on that map.
public sealed partial class ResearchSystem
{
    [Dependency] private MetaDataSystem _metaDataSystem = default!;
    [Dependency] private IViewVariablesManager _viewVariablesManager = default!;

    private void InitializeServerList()
    {
        _viewVariablesManager.GetTypeHandler<ResearchServerComponent>().AddPath(
            nameof(ResearchServerComponent.ServerName),
            (_, component) => component.ServerName,
            (uid, name, component) => SetServerName((uid, component), name));
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _viewVariablesManager.GetTypeHandler<ResearchServerComponent>().RemovePath(nameof(ResearchServerComponent.ServerName));
    }

    /// <summary>
    ///     Renames a server, in the server selection lists as well.
    /// </summary>
    public void SetServerName(Entity<ResearchServerComponent?> entity, string name)
    {
        if (!Resolve(entity.Owner, ref entity.Comp, logMissing: false))
            return;

        entity.Comp.ServerName = name;
        Dirty(entity.Owner, entity.Comp);

        AddServerToList((entity.Owner, entity.Comp));
    }

    /// <summary>
    ///     Adds a server to the list of the map it is on, or refreshes its entry.
    /// </summary>
    private void AddServerToList(Entity<ResearchServerComponent> entity)
    {
        // The server's map changes when it, or the grid it is on, is moved to another map.
        _metaDataSystem.SetFlag(entity.Owner, MetaDataFlags.ExtraTransformEvents, true);

        if (Transform(entity).MapUid is not { } mapUid)
            return;

        var listComponent = EnsureComp<KsResearchServerListComponent>(mapUid);
        var serverNetEntity = GetNetEntity(entity.Owner);
        var serverEntry = new KsResearchServerEntry(serverNetEntity, entity.Comp.Id, entity.Comp.ServerName);

        var existingIndex = listComponent.Servers.FindIndex(entry => entry.Server == serverNetEntity);
        if (existingIndex >= 0)
        {
            if (listComponent.Servers[existingIndex] == serverEntry)
                return;

            listComponent.Servers[existingIndex] = serverEntry;
        }
        else
        {
            listComponent.Servers.Add(serverEntry);
        }

        Dirty(mapUid, listComponent);
    }

    private void RemoveServerFromList(EntityUid serverUid, EntityUid? mapUid)
    {
        if (mapUid is not { } map || TerminatingOrDeleted(map))
            return;

        if (!TryComp<KsResearchServerListComponent>(map, out var listComponent))
            return;

        var serverNetEntity = GetNetEntity(serverUid);
        if (listComponent.Servers.RemoveAll(entry => entry.Server == serverNetEntity) > 0)
            Dirty(map, listComponent);
    }

    [SubscribeLocalEvent]
    private void OnServerMapUidChanged(Entity<ResearchServerComponent> entity, ref MapUidChangedEvent args)
    {
        RemoveServerFromList(entity.Owner, args.OldMap);
        AddServerToList(entity);
    }
}
