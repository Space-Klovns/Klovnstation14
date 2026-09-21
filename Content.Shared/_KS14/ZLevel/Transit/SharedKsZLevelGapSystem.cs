using Content.Shared._KS14.ZLevel.Physics;
using Robust.Shared.Map.Components;

namespace Content.Shared._KS14.ZLevel.Transit;

/// <summary>
///     Owns the maps that sit between z-levels, and the progress of whatever is crossing them.
/// </summary>
/// <remarks>
///     Creating and tearing down a gap is server work, so it lives in the server subclass. What is shared is
///         the progress: it is driven from a clock both sides already agree on, so the ride is smooth between
///         server states instead of stepping once per snapshot.
/// </remarks>
public abstract partial class SharedKsZLevelGapSystem : EntitySystem
{
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;

    [Dependency] private EntityQuery<KsZLevelComponent> _zLevelQuery = default!;
    [Dependency] private EntityQuery<MapComponent> _mapQuery = default!;
    [Dependency] protected EntityQuery<KsZLevelGapComponent> GapQuery = default!;

    /// <summary>
    ///     Catches anything falling through a z-level onto a grid that is part way up the gap.
    /// </summary>
    /// <remarks>
    ///     A gap is a map of its own, so a faller on the z-level below it passes straight through whatever is
    ///         crossing - there is no shared space for them to collide in. This is the wiring that fixes
    ///         that, and it is a broadcast query rather than anything the fall knows about gaps: the fall
    ///         asks what is in the way, and a gap is one of the things that can answer.
    /// </remarks>
    public override void Initialize()
    {
        base.Initialize();

        // Stays an explicit call: KsZLevelTransitObstructionEvent is a broadcast by-ref event, and the
        //      subscription generator reads a lone by-ref parameter as the non-ref EntityEventHandler. It
        //      does not fail the build - it simply generates nothing, and the handler is never called.
        SubscribeLocalEvent<KsZLevelTransitObstructionEvent>(OnTransitObstruction);
    }

    private void OnTransitObstruction(ref KsZLevelTransitObstructionEvent args)
    {
        if (!_zLevelQuery.TryGetComponent(args.ZLevelUid, out var zLevelComponent))
            return;

        var depth = MathF.Max(zLevelComponent.Depth, KsZLevelSystem.MinimumDepth);

        var enumerator = EntityQueryEnumerator<KsZLevelGapComponent>();
        while (enumerator.MoveNext(out var gapUid, out var gapComponent))
        {
            if (gapComponent.LowerZLevel != args.ZLevelUid)
                continue;

            // Expressed in the faller's units: a fraction of the z-level's own Depth. Taken through
            //      TotalDepth rather than assuming it equals Depth, so an admin retuning the level mid-fall
            //      moves the faller and the gap by the same amount.
            var gapHeight = gapComponent.Progress * gapComponent.TotalDepth / depth;

            // Strictly passed through this tick. A gap that has risen past the faller instead has overtaken
            //      them, and putting them on its roof would be teleporting them upwards.
            if (gapHeight >= args.FromHeight || gapHeight < args.ToHeight)
                continue;

            // Only the topmost answer matters, and only if there is something to actually land on: a gap is
            //      mostly empty, and falling past the edge of a lift should stay a fall.
            if (args.LandingMapUid != null && gapHeight <= args.LandingHeight)
                continue;

            if (!_mapQuery.TryGetComponent(gapUid, out var mapComponent) ||
                !_zLevelSystem.IsFloorSolidAt(mapComponent.MapId, args.WorldPosition))
                continue;

            args.LandingMapUid = gapUid;
            args.LandingHeight = gapHeight;
        }
    }

    /// <summary>
    ///     Moves whatever is crossing a gap to a new position within it.
    /// </summary>
    /// <param name="progress">0 at the lower z-level's floor plane, 1 at the upper one's.</param>
    public void SetGapProgress(Entity<KsZLevelGapComponent> entity, float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);

        if (MathHelper.CloseTo(entity.Comp.Progress, progress))
            return;

        entity.Comp.Progress = progress;

        // Dirtied on the client as well, though nothing here can mispredict: progress is recomputed from the
        //      clock every tick rather than integrated, so restoring the last server state and recomputing
        //      lands on the same number either way. It is kept because it costs nothing and it is what the
        //      field being networked at all is for - anything that later sets progress from something other
        //      than the clock would otherwise drift silently.
        Dirty(entity);
    }

    /// <summary>
    ///     Fills the list with every gap anchored to a z-level, ascending by how far up the gap they sit.
    /// </summary>
    /// <remarks>
    ///     Only the renderer wants this - everything else asks stack questions and is answered about the
    ///         anchor. There is no by-anchor index because there is no population to index: a gap exists only
    ///         while something is actually crossing, so the query holds a handful of entries at most.
    /// </remarks>
    /// <summary>
    ///     Whether anything is currently crossing the gap above a z-level.
    /// </summary>
    /// <remarks>
    ///     For the renderer, which has to decide whether the layered draw is worth running at all before it
    ///         knows what is in it. A viewer at the bottom of a stack has nothing below them to layer, so the
    ///         cheap single pass would otherwise be used - and a lift crossing overhead would be drawn
    ///         nowhere.
    /// </remarks>
    public bool HasGapAnchoredTo(EntityUid anchorUid)
    {
        var enumerator = EntityQueryEnumerator<KsZLevelGapComponent>();
        while (enumerator.MoveNext(out _, out var gapComponent))
        {
            if (gapComponent.LowerZLevel == anchorUid)
                return true;
        }

        return false;
    }

    /// <param name="gapEntities">List to operate on. Cleared first.</param>
    public void GetGapsAnchoredTo(EntityUid anchorUid, List<Entity<KsZLevelGapComponent>> gapEntities)
    {
        gapEntities.Clear();

        var enumerator = EntityQueryEnumerator<KsZLevelGapComponent>();
        while (enumerator.MoveNext(out var uid, out var gapComponent))
        {
            if (gapComponent.LowerZLevel != anchorUid)
                continue;

            gapEntities.Add((uid, gapComponent));
        }

        gapEntities.Sort(static (first, second) => first.Comp.Progress.CompareTo(second.Comp.Progress));
    }
}
