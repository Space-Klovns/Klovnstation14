// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Server._KS14.MovementIllusion;

public sealed partial class MovementIllusionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;

    [Dependency] private readonly EntityQuery<MovementIllusionMapComponent> _illMapQuery = default;
    [Dependency] private readonly EntityQuery<MovementIllusionFocusComponent> _illFocusQuery = default;
    [Dependency] private readonly EntityQuery<PhysicsComponent> _physicsQuery = default;

    private static readonly TimeSpan CleanupDelay = TimeSpan.FromSeconds(120f);
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(6f);
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

        var eqe = EntityQueryEnumerator<MovementIllusionBanishedComponent, PhysicsComponent>();
        while (eqe.MoveNext(out var uid, out var illusionBanishedComponent, out var physicsComponent))
        {
            if (_gameTiming.CurTime > illusionBanishedComponent.DeleteTime)
            {
                QueueDel(uid);
                continue;
            }

            _physicsSystem.SetLinearVelocity(uid, illusionBanishedComponent.Velocity, wakeBody: false, body: physicsComponent);
        }
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
        else if (_physicsQuery.TryGetComponent(args.Entity, out var physicsComponent))
        {
            var illusionBanishedComponent = EnsureComp<MovementIllusionBanishedComponent>(args.Entity);
            illusionBanishedComponent.DeleteTime = _gameTiming.CurTime + CleanupDelay;
            illusionBanishedComponent.Velocity = illusionMapComponent.Velocity;

            _physicsSystem.SetLinearVelocity(args.Entity, physicsComponent.LinearVelocity + illusionBanishedComponent.Velocity, body: physicsComponent);
        }
    }
}
