using System.Runtime.CompilerServices;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.MovementIllusion;

public sealed partial class MovementIllusionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;

    [Dependency] private readonly EntityQuery<MovementIllusionMapComponent> _illMapQuery = default!;
    [Dependency] private readonly EntityQuery<MovementIllusionFocusComponent> _illFocusQuery = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1f);
    private TimeSpan _nextUpdate = TimeSpan.MinValue;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EntParentChangedMessage>(OnParentChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gameTiming.CurTime < _nextUpdate)
            return;

        _nextUpdate = _gameTiming.CurTime + UpdateInterval;

        var eqe = EntityQueryEnumerator<MovementIllusionBanishedComponent>();
        while (eqe.MoveNext(out var uid, out var component))
            _physicsSystem.SetLinearVelocity(uid, component.Velocity, wakeBody: false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)] // The server can handle it
    private void OnParentChanged(ref EntParentChangedMessage args)
    {
        if (args.Transform.MapUid is not { } mapUid ||
            !_illMapQuery.TryGetComponent(mapUid, out var illusionMapComponent) ||
            Paused(mapUid))
            return;

        if (_illFocusQuery.HasComponent(args.Entity))
            return;

        if (_illFocusQuery.HasComponent(args.Transform.ParentUid))
            RemComp<MovementIllusionBanishedComponent>(args.Entity);
        else
        {
            var illusionBanishedComponent = AddComp<MovementIllusionBanishedComponent>(args.Entity);
            illusionBanishedComponent.Velocity = illusionMapComponent.Velocity;

            _physicsSystem.WakeBody(args.Entity);
        }
    }
}
