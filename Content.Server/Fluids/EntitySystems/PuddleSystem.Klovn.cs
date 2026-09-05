using Content.Shared._KS14.Fluids.Components;
using Content.Shared._KS14.TileEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.Timing;

namespace Content.Server.Fluids.EntitySystems;

public sealed partial class PuddleSystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsTileEffectSystem _tileEffectSystem = default!;

    private static readonly TimeSpan TileEffectUpdateInterval = TimeSpan.FromSeconds(0.5d);
    private static TimeSpan _nextTileEffectUpdate = TimeSpan.MinValue;

    private void InitialiseKlovn()
    {
        SubscribeLocalEvent<PuddleComponent, MapInitEvent>(OnPuddleMapInit);
    }

    private void OnPuddleMapInit(Entity<PuddleComponent> entity, ref MapInitEvent args)
    {
        entity.Comp.LastTileEffectUpdate = _gameTiming.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        if (curTime < _nextTileEffectUpdate)
            return;

        _nextTileEffectUpdate = curTime + TileEffectUpdateInterval;

        var eqe = EntityQueryEnumerator<TileEffectPuddleComponent, PuddleComponent>();
        while (eqe.MoveNext(out var uid, out _, out var puddleComponent))
            TryUpdateTileEffects((uid, puddleComponent), curTime);
    }

    public void TryUpdateTileEffects(Entity<PuddleComponent> entity, TimeSpan curTime, bool careAboutTime = true)
    {
        if (entity.Comp.Solution is not { } solutionEntity)
            return;

        TimeSpan deltaTime = TimeSpan.Zero;
        if (careAboutTime)
        {
            deltaTime = curTime - entity.Comp.LastTileEffectUpdate;
            if (deltaTime < TimeSpan.Zero)
                return;
        }

        var solution = solutionEntity.Comp.Solution;
        var scale = careAboutTime ? (float)deltaTime.TotalSeconds : 1f;
        if (!_tileEffectSystem.TryUpdateTileEffects(entity.Owner, null, solution, scale: scale))
            return;

        if (solution.Volume == FixedPoint2.Zero)
        {
            QueueDel(entity);
            return;
        }

        _solutionContainerSystem.UpdateChemicals(solutionEntity);
        entity.Comp.LastTileEffectUpdate = curTime;
        Dirty(solutionEntity);
    }
}
