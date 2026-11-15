using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.LawnMower;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;

namespace Content.Client._KS14.LawnMower;

public sealed partial class KsLawnMowerAppearanceSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedKsLawnMowerSystem _lawnMowerSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_configurationManager, KsCCVars.ClientLocale, _ => RefreshNames());
    }

    [SubscribeLocalEvent]
    private void OnAppearanceChanged(Entity<KsLawnMowerComponent> entity, ref AppearanceChangeEvent args)
    {
        RefreshName(entity.Owner);
    }

    private void RefreshNames()
    {
        var query = EntityQueryEnumerator<KsLawnMowerComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RefreshName(uid);
        }
    }

    private void RefreshName(EntityUid uid)
    {
        // A mower bought from the uplink already has the emagged prototype's localized name.
        if (_appearanceSystem.TryGetData<bool>(uid, KsLawnMowerVisuals.SafetyDisabled, out var disabled) && disabled &&
            MetaData(uid).EntityPrototype?.ID != SharedKsLawnMowerSystem.EmaggedPrototype.Id)
        {
            _lawnMowerSystem.ApplyEmaggedName(uid);
        }
    }
}
