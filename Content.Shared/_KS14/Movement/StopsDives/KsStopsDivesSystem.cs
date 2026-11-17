using System.Numerics;
using Content.Shared.Movement.Components;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Movement.StopsDives;

/// <summary>
///     Ends a dive where it runs into something with <see cref="KsStopsDivesComponent"/>.
/// </summary>
/// <remarks>
///     <para>
///         A dive is a throw of the diver (<c>SharedJumpAbilitySystem</c>), so ending it is landing the throw and
///             stopping it, which is also what tells the jump system it is over. Shared: a player's own dive is
///             predicted, and stops on their client as it does on the server. Only a contact that starts stops a dive,
///             so diving out of the wire one is standing in works.
///     </para>
///     <para>
///         Stopping a throw destroys its fixture, and with it contacts. A client applying a state raises the collisions
///             of its predicted bodies again from a list of contacts it gathered beforehand
///             (<c>PhysicsSystem.ResetContacts</c>), and throws on the first one that has since been destroyed. A dive
///             caught then is stopped before the next physics step instead.
///     </para>
/// </remarks>
public sealed partial class KsStopsDivesSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private ThrownItemSystem _thrownItemSystem = default!;
    [Dependency] private EntityQuery<ActiveLeaperComponent> _activeLeaperQuery = default!;
    [Dependency] private EntityQuery<ThrownItemComponent> _thrownItemQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    /// <summary>
    ///     Divers caught while a state was being applied, to stop before the next physics step.
    /// </summary>
    private readonly List<EntityUid> _caughtDuringState = new();

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<KsStopsDivesComponent> entity, ref StartCollideEvent args)
    {
        if (!IsDiving(args.OtherEntity))
            return;

        if (_gameTiming.ApplyingState)
            _caughtDuringState.Add(args.OtherEntity);
        else
            Stop(args.OtherEntity);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_caughtDuringState.Count == 0)
            return;

        foreach (var diverUid in _caughtDuringState)
        {
            Stop(diverUid);
        }

        _caughtDuringState.Clear();
    }

    private bool IsDiving(EntityUid uid)
    {
        // The jump system may have taken the leaper off already this very contact: deferred, so it is still here.
        return _activeLeaperQuery.HasComp(uid) && _thrownItemQuery.HasComp(uid);
    }

    private void Stop(EntityUid diverUid)
    {
        if (!IsDiving(diverUid) ||
            !_thrownItemQuery.TryComp(diverUid, out var thrownItemComponent) ||
            !_physicsQuery.TryComp(diverUid, out var physicsComponent))
            return;

        _physicsSystem.SetLinearVelocity(diverUid, Vector2.Zero, body: physicsComponent);
        _thrownItemSystem.LandComponent(diverUid, thrownItemComponent, physicsComponent, playSound: false);
        _thrownItemSystem.StopThrow(diverUid, thrownItemComponent);
    }
}
