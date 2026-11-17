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
    private readonly List<(EntityUid Thrown, EntityUid Target, int Priority)> _pendingHits = new();

    /// <summary>
    ///     The list being raised: a hit can start another throw, whose hits must wait for their own step.
    /// </summary>
    private readonly List<(EntityUid Thrown, EntityUid Target, int Priority)> _raisingHits = new();

    /// <summary>
    ///     Where each thrown thing was when its hits began to be raised. Scratch for <see cref="OnPhysicsAfterSolve"/>.
    /// </summary>
    private readonly Dictionary<EntityUid, EntityUid> _parentsBeforeHits = new();

    private void QueueHit(EntityUid thrown, EntityUid target)
    {
        // Each of a thrown thing's fixtures reports the same hit.
        foreach (var (pendingThrown, pendingTarget, _) in _pendingHits)
        {
            if (pendingThrown == thrown && pendingTarget == target)
                return;
        }

        _pendingHits.Add((thrown, target, _hitPriorityQuery.CompOrNull(target)?.Priority ?? 0));
    }

    [SubscribeLocalEvent]
    private void OnPhysicsAfterSolve(ref PhysicsUpdateAfterSolveEvent args)
    {
        if (_pendingHits.Count == 0)
            return;

        _raisingHits.Clear();
        _raisingHits.AddRange(_pendingHits);
        _pendingHits.Clear();

        // A stable sort by hand: equal priorities keep the order physics found them in, as before.
        for (var i = 1; i < _raisingHits.Count; i++)
        {
            var hit = _raisingHits[i];
            var j = i - 1;
            while (j >= 0 && _raisingHits[j].Priority < hit.Priority)
            {
                _raisingHits[j + 1] = _raisingHits[j];
                j--;
            }

            _raisingHits[j + 1] = hit;
        }

        _parentsBeforeHits.Clear();
        foreach (var (thrown, target, _) in _raisingHits)
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
