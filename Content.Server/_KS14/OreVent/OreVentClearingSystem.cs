using Content.Server.Gatherable.Components;
using Content.Shared._KS14.OreVent;
using Content.Shared.Destructible;
using Robust.Shared.Audio.Systems;

namespace Content.Server._KS14.OreVent;

/// <summary>
///     Destroys the minable rock around an ore vent as it clears. Nothing is dropped:
///         the vent is what's worth mining.
/// </summary>
public sealed partial class OreVentClearingSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private SharedDestructibleSystem _destructibleSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;

    private readonly HashSet<Entity<GatherableComponent>> _rocks = [];

    [SubscribeLocalEvent]
    private void OnClearing(Entity<OreVentComponent> entity, ref OreVentClearingEvent args)
    {
        _rocks.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(entity).Coordinates, args.Radius, _rocks, LookupFlags.Static);

        if (_rocks.Count == 0)
            return;

        foreach (var rock in _rocks)
            _destructibleSystem.DestroyEntity(rock.Owner);

        _audioSystem.PlayPvs(entity.Comp.ClearingSound, entity.Owner);
    }
}
