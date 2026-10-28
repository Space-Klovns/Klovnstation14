using Content.Shared._KS14.IoC;
using Content.Shared._KS14.TileEffects.Effects;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.TileEffects;

public sealed partial class KsTileEffectSystem : EntitySystem
{
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        _systemCollectionHookManager.HookAction(OnAction);
    }

    /// <summary>
    ///     Per reagent, the coolest open flame that lights it (see <see cref="CheckTemperatureTileEffect.LitByFlame"/>), or
    ///         null if no flame does. Filled as reagents are asked about, and cleared when reagents reload.
    /// </summary>
    private readonly Dictionary<string, float?> _flameLightingTemperatures = new();

    private void ReloadReagents(IDependencyCollection dependencyCollection)
    {
        _flameLightingTemperatures.Clear();

        foreach (var reagentPrototype in ProtoMan.EnumeratePrototypes<ReagentPrototype>())
        {
            foreach (var tileEffect in reagentPrototype.KsTileEffects)
                tileEffect.Initialize(dependencyCollection);

            foreach (var tileReaction in reagentPrototype.TileReactions)
            {
                if (tileReaction is KsTileReaction ksTileReaction)
                    ksTileReaction.Initialize(dependencyCollection);
            }
        }
    }

    private void OnAction(IDependencyCollection dependencyCollection)
        => ReloadReagents(dependencyCollection);

    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.Modified.Contains(typeof(ReagentPrototype)))
            return;

        ReloadReagents(_systemCollectionHookManager.DependencyCollection);
    }

    /// <summary>
    ///     Injects <paramref name="tileEffect"/> if nothing has yet, or if what did was a client's systems from before it
    ///         last connected (see <see cref="KsTileEffect.IsInitializedFor"/>).
    /// </summary>
    public void EnsureInitialized(KsTileEffect tileEffect)
    {
        if (!tileEffect.IsInitializedFor(this))
            tileEffect.Initialize(_systemCollectionHookManager.DependencyCollection);
    }

    /// <summary>
    ///     Whether an open flame this hot would set something in the solution alight, however cold the solution is.
    /// </summary>
    public bool CanBeLitByFlame(Solution solution, float flameTemperature)
    {
        foreach (var (reagent, quantity) in solution.Contents)
        {
            if (quantity <= FixedPoint2.Zero)
                continue;

            if (GetFlameLightingTemperature(reagent.Prototype) is { } minimum && flameTemperature >= minimum)
                return true;
        }

        return false;
    }

    private float? GetFlameLightingTemperature(string reagentId)
    {
        if (_flameLightingTemperatures.TryGetValue(reagentId, out var cached))
            return cached;

        float? coolest = null;
        if (ProtoMan.TryIndex<ReagentPrototype>(reagentId, out var reagentPrototype))
        {
            foreach (var tileEffect in reagentPrototype.KsTileEffects)
                FindFlameLightingTemperature(tileEffect, ref coolest);
        }

        _flameLightingTemperatures[reagentId] = coolest;
        return coolest;
    }

    private static void FindFlameLightingTemperature(KsTileEffect tileEffect, ref float? coolest)
    {
        switch (tileEffect)
        {
            case CheckTemperatureTileEffect { LitByFlame: true } checkTemperature:
                var minimum = checkTemperature.Minimum ?? 0f;
                coolest = coolest is { } current ? MathF.Min(current, minimum) : minimum;
                break;
            case LogicAndTileEffect logicAnd:
                foreach (var condition in logicAnd.Conditions)
                    FindFlameLightingTemperature(condition, ref coolest);
                break;
        }
    }

    /// <param name="flameTemperature">See <see cref="KsTileEffectContext.FlameTemperature"/>.</param>
    /// <param name="refreshedWithin">See <see cref="KsTileEffectContext.RefreshedWithin"/>.</param>
    public bool TryUpdateTileEffects(Entity<TransformComponent?> entity, TileRef? tileRef, Solution solution, float scale = 1f, float? flameTemperature = null, TimeSpan? refreshedWithin = null)
    {
        entity.Comp ??= Transform(entity);
        if (entity.Comp.GridUid is not { } gridUid ||
            !_mapGridQuery.TryGetComponent(gridUid, out var gridComponent))
            return false;

        var anythingHappened = false;
        var context = new KsTileEffectContext(0f, solution, null, FlameTemperature: flameTemperature, RefreshedWithin: refreshedWithin);

        for (var i = solution.Contents.Count - 1; i >= 0; i--)
        {
            var (reagent, quantity) = solution.Contents[i];
            var reagentPrototype = ProtoMan.Index<ReagentPrototype>(reagent.Prototype);

            // KS14 - RemovedVolume is per reagent; it was carrying over from the previous reagent otherwise.
            context.RemovedVolume = 0f;
            context.Reagent = reagent;

            foreach (var tileEffect in reagentPrototype.KsTileEffects)
            {
                // A prototype reload can hand out new instances without telling this system, so they are checked here,
                //     where an uninjected one would otherwise throw.
                EnsureInitialized(tileEffect);

                if (tileRef is not { })
                    tileRef = _mapSystem.GetTileRef((gridUid, gridComponent), entity.Comp.Coordinates);

                anythingHappened |= tileEffect.Execute(tileRef!.Value, scale, ref context);
                if (context.RemovedVolume >= (float)quantity)
                    break;
            }

            var removedVolume = context.RemovedVolume;
            if (removedVolume <= 0f)
                continue;

            solution.RemoveReagent(reagent, removedVolume);
        }

        return anythingHappened;
    }
}
