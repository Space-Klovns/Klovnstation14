using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Lawnmower;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.Lawnmower;

public sealed partial class KsLawnmowerAppearanceSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedKsLawnmowerSystem _lawnMowerSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_configurationManager, KsCCVars.ClientLocale, _ => RefreshNames());
    }

    [SubscribeLocalEvent]
    private void OnAppearanceChanged(Entity<KsLawnmowerComponent> entity, ref AppearanceChangeEvent args)
    {
        RefreshName(entity);
    }

    private void RefreshNames()
    {
        var query = EntityQueryEnumerator<KsLawnmowerComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            RefreshName((uid, component));
        }
    }

    private void RefreshName(Entity<KsLawnmowerComponent> entity)
    {
        // A mower spawned from the emagged prototype already has its localized name.
        if (_appearanceSystem.TryGetData<bool>(entity.Owner, KsLawnmowerVisuals.SafetyDisabled, out var disabled) && disabled &&
            MetaData(entity.Owner).EntityPrototype?.ID != entity.Comp.EmaggedPrototype.Id)
        {
            _lawnMowerSystem.ApplyEmaggedName(entity);
        }
    }
}
