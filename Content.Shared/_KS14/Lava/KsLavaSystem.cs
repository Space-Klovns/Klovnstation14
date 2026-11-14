using Content.Shared._KS14.Burning;
using Content.Shared.Chat;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Lava;

/// <summary>
///     Not my proudest code yet
/// </summary>
public sealed partial class KsLavaSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedChatSystem _chatSystem = default!;
    [Dependency] private KsItemBurningSystem _itemBurningSystem = default!;
    [Dependency] private EntityQuery<ItemComponent> _itemQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;

    /// <summary>
    ///     The lava fixture items touch. Defined on the <c>FloorLavaEntity</c> prototype.
    /// </summary>
    public const string ItemFixtureId = "ksItemFixture";

    /// <summary>
    ///     List of occupied lava tiles on each grid or whatever.
    /// </summary>
    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _lavaMap = [];

    // tbh if i really tryharded, this wouldnt be necessary either
    private static readonly Vector2i[] ValidDirections = [Vector2i.Up, Vector2i.UpLeft, Vector2i.UpRight, Vector2i.Left, Vector2i.Right, Vector2i.Down, Vector2i.DownLeft, Vector2i.DownRight];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>((_) => _lavaMap.Clear());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var sinkingEqe = EntityQueryEnumerator<KsLavaSinkingComponent>();
        while (sinkingEqe.MoveNext(out var uid, out var sinkingComponent))
        {
            if (_gameTiming.CurTime < sinkingComponent.SinkTime)
                continue;

            PredictedQueueDel(uid);
        }
    }

    /// <summary>
    ///     Keeps mobs off the item fixture, as <see cref="Fluids.KsPuddleTouchSystem"/> does for puddles:
    ///         <c>FlyingMobMask</c> is exactly <c>ItemMask</c>, so without this a flying mob would be burned by lava it
    ///         flies over. Walking mobs still touch the lava through its other fixture.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPreventCollide(Entity<KsLavaComponent> entity, ref PreventCollideEvent args)
    {
        // Cheapest test first: this is raised for every contact either of the lava's fixtures could make.
        if (!args.OtherFixture.Hard || !_mobStateQuery.HasComp(args.OtherEntity))
            return;

        if (!_fixturesQuery.TryComp(entity.Owner, out var fixturesComponent) ||
            !fixturesComponent.Fixtures.TryGetValue(ItemFixtureId, out var itemFixture) ||
            !ReferenceEquals(itemFixture, args.OurFixture))
            return;

        args.Cancelled = true;
    }

    [SubscribeLocalEvent]
    private void OnStepTriggerAttempt(Entity<KsLavaComponent> entity, ref StepTriggerAttemptEvent args)
    {
        args.Continue = true;
    }

    [SubscribeLocalEvent]
    private void OnStepTriggered(Entity<KsLavaComponent> entity, ref StepTriggeredOffEvent args)
    {
        if (HasComp<KsLavaProofComponent>(args.Tripper))
            return;

        // Loose items don't sink, they catch fire and slowly burn away (tgstation's lava burn_stuff).
        if (_itemQuery.HasComp(args.Tripper) && !_mobStateQuery.HasComp(args.Tripper))
        {
            _itemBurningSystem.Ignite(args.Tripper, initialDamage: entity.Comp.ItemIgniteDamage);
            return;
        }

        if (!_damageableSystem.TryChangeDamage(args.Tripper, entity.Comp.Damage, origin: entity.Owner))
            return;

        _stunSystem.TryKnockdown(args.Tripper, entity.Comp.KnockdownDuration, refresh: true, autoStand: false);

        // If surrounded totally by lava, ash them
        if (HasComp<KsLavaSinkingComponent>(args.Tripper) ||
            !IsSurrounded(entity))
            return;

        var sinkingComponent = AddComp<KsLavaSinkingComponent>(args.Tripper);
        sinkingComponent.StartTime = _gameTiming.CurTime;
        sinkingComponent.SinkTime = _gameTiming.CurTime + entity.Comp.SinkDuration;
        Dirty(args.Tripper, sinkingComponent);

        _physicsSystem.SetBodyType(args.Tripper, BodyType.Static);
        _transformSystem.SetCoordinates(args.Tripper, Transform(entity.Owner).Coordinates);

        if (!_netManager.IsServer)
            return;

        if (_robustRandom.Prob(0.02f))
            _chatSystem.TrySendInGameICMessage(args.Tripper, "gives a thumbs up as they melt to death…", InGameICChatType.Emote, false); // i wonder what this is in reference to
        else
            _chatSystem.TryEmoteWithChat(args.Tripper, "Scream"); // yes i know
    }

    [SubscribeLocalEvent]
    private void OnEntParentChanged(Entity<KsLavaComponent> entity, ref EntParentChangedMessage args)
    {
        var transformComponent = args.Transform;
        var newGridUid = transformComponent.GridUid;

        if (entity.Comp.LocalGridUid is { } oldGridUid &&
            newGridUid != oldGridUid &&
            _lavaMap.TryGetValue(oldGridUid, out var oldLocalMap))
        {
            oldLocalMap.Remove(entity.Comp.LocalTile);

            entity.Comp.LocalGridUid = null;
            Dirty(entity);

            if (oldLocalMap.Count == 0)
                _lavaMap.Remove(oldGridUid);
        }

        if (_transformSystem.TryGetGridTilePosition((entity.Owner, transformComponent), out var tilePos))
        {
            var localMap = _lavaMap.GetOrNew(newGridUid!.Value);
            if (!localMap.Add(tilePos))
                return;

            entity.Comp.LocalGridUid = newGridUid;
            entity.Comp.LocalTile = tilePos;
            Dirty(entity);
        }
    }

    /// <returns>True if the lava is surrounded on all sides by more lava, false otherwise (when the lava is an edge).</returns>
    public bool IsSurrounded(Entity<KsLavaComponent> entity)
    {
        if (entity.Comp.LocalGridUid is not { } gridUid ||
            !_lavaMap.TryGetValue(gridUid, out var localMap))
            return false;

        foreach (var direction in ValidDirections)
        {
            if (localMap.Contains(entity.Comp.LocalTile + direction))
                continue;

            // End early and return false, as this direction is empty
            return false;
        }

        return true;
    }
}
