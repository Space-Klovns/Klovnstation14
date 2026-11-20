using Content.Shared._KS14.Throwing;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.Throwing;

// Hits found during a physics step are gathered and raised after it, in order of KsThrowHitPriorityComponent.
public sealed partial class ThrownItemSystem
{
    [Dependency] private EntityQuery<KsThrowHitPriorityComponent> _hitPriorityQuery = default!;

    /// <summary>
    ///     Hits found this physics step, in the order physics found them. Drained when the step ends.
    /// </summary>
    private readonly List<(EntityUid Thrown, EntityUid Target, int Priority, int Order)> _pendingHits = new();

    /// <summary>
    ///     The same hits by who hit what, to tell a repeat without going through the list.
    /// </summary>
    private readonly HashSet<(EntityUid Thrown, EntityUid Target)> _pendingHitPairs = new();

    /// <summary>
    ///     The list being raised: a hit can start another throw, whose hits must wait for their own step.
    /// </summary>
    private readonly List<(EntityUid Thrown, EntityUid Target, int Priority, int Order)> _raisingHits = new();

    /// <summary>
    ///     Highest priority first; equal priorities keep the order physics found them in, as before.
    /// </summary>
    private static readonly Comparison<(EntityUid Thrown, EntityUid Target, int Priority, int Order)> HitOrder =
        (a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : a.Order.CompareTo(b.Order);

    /// <summary>
    ///     Where each thrown thing was when its hits began to be raised. Scratch for <see cref="OnPhysicsAfterSolve"/>.
    /// </summary>
    private readonly Dictionary<EntityUid, EntityUid> _parentsBeforeHits = new();

    private void QueueHit(EntityUid thrown, EntityUid target)
    {
        // Each of a thrown thing's fixtures reports the same hit.
        if (!_pendingHitPairs.Add((thrown, target)))
            return;

        _pendingHits.Add((thrown, target, _hitPriorityQuery.CompOrNull(target)?.Priority ?? 0, _pendingHits.Count));
    }

    [SubscribeLocalEvent]
    private void OnPhysicsAfterSolve(ref PhysicsUpdateAfterSolveEvent args)
    {
        if (_pendingHits.Count == 0)
            return;

        _raisingHits.Clear();
        _raisingHits.AddRange(_pendingHits);
        _pendingHits.Clear();
        _pendingHitPairs.Clear();
        _raisingHits.Sort(HitOrder);

        _parentsBeforeHits.Clear();
        foreach (var (thrown, target, _, _) in _raisingHits)
        {
            // An earlier hit can have ended the throw, or deleted either of them.
            if (TerminatingOrDeleted(thrown) || TerminatingOrDeleted(target) ||
                !TryComp<ThrownItemComponent>(thrown, out var component))
            {
                continue;
            }

            // Or put it somewhere else: embedded in what it hit, caught in a container. When hits were raised inside
            // the physics step that ended its other contacts on the spot. Here it has to be asked.
            var parent = Transform(thrown).ParentUid;
            if (!_parentsBeforeHits.TryAdd(thrown, parent) && _parentsBeforeHits[thrown] != parent)
                continue;

            ThrowCollideInteraction(component, thrown, target);
        }
    }
}
