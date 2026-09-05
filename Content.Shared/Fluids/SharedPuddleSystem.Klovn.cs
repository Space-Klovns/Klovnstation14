using Content.Shared._KS14.Fluids.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Fluids.Components;

namespace Content.Shared.Fluids;

public abstract partial class SharedPuddleSystem
{
    private readonly HashSet<int> _ksReagentsWithTileEffects = [];

    private void KsOnReagentsReloaded()
    {
        _ksReagentsWithTileEffects.Clear();
        _ksReagentsWithTileEffects.TrimExcess();

        foreach (var reagentPrototype in _prototypeManager.EnumeratePrototypes<ReagentPrototype>())
        {
            if (reagentPrototype.KsTileEffects.Length == 0)
                continue;

            _ksReagentsWithTileEffects.Add(reagentPrototype.ID.GetHashCode());
        }

        var eqe = EntityQueryEnumerator<PuddleComponent>();
        while (eqe.MoveNext(out var uid, out var puddleComponent))
        {
            if (puddleComponent.Solution?.Comp.Solution is not { } solution)
                continue;

            UpdateTileEffects(uid, solution);
        }
    }

    private void UpdateTileEffects(EntityUid puddleUid, Solution solution)
    {
        var containsTileEffects = false;
        foreach (var (reagentId, _) in solution.Contents)
        {
            if (!_ksReagentsWithTileEffects.Contains(reagentId.Prototype.GetHashCode()))
                continue;

            containsTileEffects = true;
            break;
        }

        if (containsTileEffects)
            EnsureComp<TileEffectPuddleComponent>(puddleUid);
        else
            RemComp<TileEffectPuddleComponent>(puddleUid);
    }
}
