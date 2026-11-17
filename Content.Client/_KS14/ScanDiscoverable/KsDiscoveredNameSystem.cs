using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ScanDiscoverable.Base;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.ScanDiscoverable;

/// <summary>
///     Shows a discovered entity's name in this client's language, over the server's copy of it.
/// </summary>
/// <remarks>
///     <para>
///         This only covers the client's own copy of the name. Whenever the server sends this entity's metadata again,
///             <c>MetaDataSystem.OnMetaDataHandle</c> writes the server's name straight over it and raises nothing, so
///             the server's language shows until the next language change or reconnect. Geysers and ore vents rarely
///             change, so it should be rare, but anything that renames or redescribes one would trigger it.
///     </para>
///     <para>
///         Anything that reads the name on the server gets the server's language too: logs, and any string the server
///             formats and sends. To follow the reader's language there, pass the locale key along instead.
///     </para>
/// </remarks>
public sealed partial class KsDiscoveredNameSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        // The localization manager subscribed first, at startup, so the new culture is already in place here.
        Subs.CVar(_configurationManager, KsCCVars.ClientLocale, _ => ApplyAll());
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsDiscoveredNameComponent> entity, ref ComponentStartup args)
    {
        Apply(entity);
    }

    [SubscribeLocalEvent]
    private void OnAfterState(Entity<KsDiscoveredNameComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        Apply(entity);
    }

    private void ApplyAll()
    {
        var query = AllEntityQuery<KsDiscoveredNameComponent>();
        while (query.MoveNext(out var uid, out var discoveredNameComponent))
        {
            Apply((uid, discoveredNameComponent));
        }
    }

    private void Apply(Entity<KsDiscoveredNameComponent> entity)
    {
        // Added to a live entity, this starts up before it has a name: a state adds it and starts it before handling that
        //      state, and predicting a discovery sets the name after adding it. The name comes through OnAfterState,
        //      or from the discovery itself.
        if (entity.Comp.Name == default)
            return;

        _metaDataSystem.SetEntityName(entity.Owner, Loc.GetString(entity.Comp.Name));
    }
}
