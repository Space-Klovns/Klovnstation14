using Content.Shared._KS14.IoC;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.TileEffects;

public sealed partial class KsTileEffectSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        _systemCollectionHookManager.HookAction(OnAction);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void ReloadReagents(IDependencyCollection dependencyCollection)
    {
        foreach (var reagentPrototype in _prototypeManager.EnumeratePrototypes<ReagentPrototype>())
        {
            foreach (var tileEffect in reagentPrototype.KsTileEffects)
                tileEffect.Initialize(dependencyCollection);
        }
    }

    private void OnAction(IDependencyCollection dependencyCollection)
        => ReloadReagents(dependencyCollection);

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.Modified.Contains(typeof(ReagentPrototype)))
            return;

        ReloadReagents(_systemCollectionHookManager.DependencyCollection);
    }

    public bool TryUpdateTileEffects(Entity<TransformComponent?> entity, TileRef? tileRef, Solution solution, float scale = 1f)
    {
        entity.Comp ??= Transform(entity);
        if (entity.Comp.GridUid is not { } gridUid ||
            !_mapGridQuery.TryGetComponent(gridUid, out var gridComponent))
            return false;

        var anythingHappened = false;
        var tileEffectReagentData = new KsTileEffectReagentData(0f, solution, null);

        for (var i = solution.Contents.Count - 1; i >= 0; i--)
        {
            var (reagent, quantity) = solution.Contents[i];
            var reagentPrototype = _prototypeManager.Index<ReagentPrototype>(reagent.Prototype);

            foreach (var tileEffect in reagentPrototype.KsTileEffects)
            {
                if (tileRef is not { })
                    tileRef = _mapSystem.GetTileRef((gridUid, gridComponent), entity.Comp.Coordinates);

                anythingHappened |= tileEffect.Execute(tileRef!.Value, scale, ref tileEffectReagentData);
                if (tileEffectReagentData.RemovedVolume >= (float)quantity)
                    break;
            }

            var removedVolume = tileEffectReagentData.RemovedVolume;
            if (removedVolume <= 0f)
                continue;

            solution.RemoveReagent(reagent, removedVolume);
        }

        return anythingHappened;
    }
}
