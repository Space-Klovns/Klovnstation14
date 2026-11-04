using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ScanDiscoverable.Base;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.ScanDiscoverable;

/// <summary>
///     Shows a discovered entity's name in this client's language, over the server's copy of it.
/// </summary>
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
        _metaDataSystem.SetEntityName(entity.Owner, Loc.GetString(entity.Comp.Name));
    }
}
