using Content.Server._KS14.NPC.Passages;
using Content.Shared._KS14.Ladders;

namespace Content.Server._KS14.Ladders;

/// <summary>
///     Ladders as passages for NPCs (<see cref="KsNpcPassageSystem"/>): one leads where it is linked to, and is taken by
///         climbing it from beside it.
/// </summary>
public sealed partial class KsLadderNpcPassageSystem : EntitySystem
{
    [Dependency] private KsLadderSystem _ladderSystem = default!;

    /// <summary>
    ///     How long past the climb's own time an NPC waits for the climb to have happened.
    /// </summary>
    private static readonly TimeSpan ClimbMargin = TimeSpan.FromSeconds(0.25);

    [SubscribeLocalEvent]
    private void OnGetDestination(Entity<KsLadderComponent> entity, ref KsGetNpcPassageDestinationEvent args)
    {
        if (_ladderSystem.TryGetDestination(entity, out var destination))
            args.Destination = destination;
    }

    [SubscribeLocalEvent]
    private void OnTake(Entity<KsLadderComponent> entity, ref KsNpcTakePassageEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (_ladderSystem.TryStartClimb(entity, args.NpcUid) && entity.Comp.UseDelay is { } delay)
            args.Wait = delay + ClimbMargin;
    }
}
