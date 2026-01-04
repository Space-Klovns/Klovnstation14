// SPDX-License-Identifier: AGPL-3.0-or-later
using Content.Goobstation.Common.Hands;
using Content.Shared.ActionBlocker;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Random.Helpers;
using Content.Shared.Throwing;
using Content.Shared.Stacks;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Containers;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Trauma.Shared.Hands;

/// <summary>
/// Predicting hand-related stuff that is serverside for no reason.
/// </summary>
public sealed class PredictedHandsSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<VirtualItemComponent> _virtualQuery;

    /// <summary>
    /// Items dropped when the holder falls down will be launched in
    /// a direction offset by up to this many degrees from the holder's
    /// movement direction.
    /// </summary>
    private const float DropHeldItemsSpread = 45;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HandsComponent, DisarmedEvent>(OnDisarmed, before: new[] {typeof(SharedStunSystem), typeof(SharedStaminaSystem)});

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.ThrowItemInHand, new PointerInputCmdHandler(HandleThrowItem))
            .Register<PredictedHandsSystem>();

        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _virtualQuery = GetEntityQuery<VirtualItemComponent>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        CommandBinds.Unregister<PredictedHandsSystem>();
    }

    private void OnDisarmed(Entity<HandsComponent> ent, ref DisarmedEvent args)
    {
        if (args.Handled)
            return;

        var seed = SharedRandomExtensions.HashCodeCombine((int) _timing.CurTick.Value, GetNetEntity(ent).Id);
        var rand = new System.Random(seed);
        if (!rand.Prob(args.DisarmProbability))
            return;

        args.WasDisarmed = true;
        // Break any pulls
        if (TryComp(ent, out PullerComponent? puller) && TryComp(puller.Pulling, out PullableComponent? pullable))
            _pulling.TryStopPull(puller.Pulling.Value, pullable);

        var offset = rand.NextAngle().RotateVec(new Vector2(rand.NextFloat(1, 1.5f), 0));
        var coords = _transform.GetMoverCoordinates(args.Target).Offset(offset);
        if (!ThrowHeldItem(args.Target, coords))
            return;

        args.Handled = true; // Successful disarm
    }

    #region Interactions

    private bool HandleThrowItem(ICommonSession? session, EntityCoordinates coordinates, EntityUid entity)
    {
        if (session?.AttachedEntity is not {} player || !Exists(player) || !coordinates.IsValid(EntityManager))
            return false;

        // <Goob>
        if (_hands.TryGetActiveItem(player, out var item) && _virtualQuery.TryComp(item, out var virtComp))
        {
            var virtEv = new VirtualItemDropAttemptEvent(virtComp.BlockingEntity, player, item.Value, true);
            RaiseLocalEvent(player, ref virtEv);
            if (virtEv.Cancelled) return false;

            RaiseLocalEvent(virtComp.BlockingEntity, ref virtEv);
            if (virtEv.Cancelled) return false;
        }
        // </Goob>

        ThrowHeldItem(player, coordinates);
        return false; // always send to server
    }

    /// <summary>
    /// Throw the player's currently held item.
    /// </summary>
    public bool ThrowHeldItem(EntityUid player, EntityCoordinates coordinates, float minDistance = 0.1f)
    {
        if (_container.IsEntityInContainer(player) ||
            !TryComp(player, out HandsComponent? hands) ||
            !_hands.TryGetActiveItem((player, hands), out var throwEnt) ||
            !_actionBlocker.CanThrow(player, throwEnt.Value))
            return false;

        // <Goob> - added throwing for grabbed mobs, moved direction.
        var direction = _transform.ToMapCoordinates(coordinates).Position - _transform.GetWorldPosition(player);

        if (_virtualQuery.TryComp(throwEnt, out var virt))
        {
            var virtEv = new VirtualItemThrownEvent(virt.BlockingEntity, player, throwEnt.Value, direction);
            RaiseLocalEvent(player, ref virtEv);
            RaiseLocalEvent(virt.BlockingEntity, ref virtEv);
        }
        // </Goob>

        if (_timing.CurTime < hands.NextThrowTime)
            return false;

        hands.NextThrowTime = _timing.CurTime + hands.ThrowCooldown;
        Dirty(player, hands);

        if (TryComp(throwEnt, out StackComponent? stack) && stack.Count > 1 && stack.ThrowIndividually)
        {
            if (_stack.Split((throwEnt.Value, stack), 1, Transform(player).Coordinates) is not {} splitStack)
                return false;

            throwEnt = splitStack;
        }

        if (direction == Vector2.Zero)
            return true;

        var length = direction.Length();
        var distance = Math.Clamp(length, minDistance, hands.ThrowRange);
        direction *= distance / length;

        var throwSpeed = hands.BaseThrowspeed;

        // Let other systems change the thrown entity (useful for virtual items)
        // or the throw strength.
        // <Goob> - added thrower's velocity for inertia
        var holderVelocity = _physics.GetMapLinearVelocity(player);
        var modifier = MathF.Max(0f, Vector2.Dot(direction.Normalized(), holderVelocity));
        var ev = new BeforeThrowEvent(throwEnt.Value, direction * (1f + modifier * 0.1f), throwSpeed * (1f + modifier * 0.05f), player);
        // </Goob>
        RaiseLocalEvent(player, ref ev);

        if (ev.Cancelled)
            return true;

        // This can grief the above event so we raise it afterwards
        if (_hands.IsHolding((player, hands), throwEnt, out _) && !_hands.TryDrop(player, throwEnt.Value))
            return false;

        _throwing.TryThrow(
            ev.ItemUid,
            ev.Direction,
            ev.ThrowSpeed,
            ev.PlayerUid,
            compensateFriction: !HasComp<LandAtCursorComponent>(ev.ItemUid));
        return true;
    }

    #endregion
}
