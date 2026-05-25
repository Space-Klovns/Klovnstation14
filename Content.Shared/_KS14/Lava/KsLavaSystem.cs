using Content.Shared.Chat;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Lava;

/// <summary>
///     Not my proudest code yet
/// </summary>
public sealed class KsLavaSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly SharedStunSystem _stunSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly SharedChatSystem _chatSystem = default!;

    /// <summary>
    ///     List of occupied lava tiles on each grid or whatever.
    /// </summary>
    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _lavaMap = [];

    // tbh if i really tryharded, this wouldnt be necessary either
    private static readonly Vector2i[] ValidDirections = [Vector2i.Up, Vector2i.UpLeft, Vector2i.UpRight, Vector2i.Left, Vector2i.Right, Vector2i.Down, Vector2i.DownLeft, Vector2i.DownRight];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsLavaComponent, StepTriggerAttemptEvent>(OnStepTriggerAttempt);
        SubscribeLocalEvent<KsLavaComponent, StepTriggeredOffEvent>(OnStepTriggered);

        SubscribeLocalEvent<KsLavaComponent, EntParentChangedMessage>(OnEntParentChanged);
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

    private void OnStepTriggerAttempt(Entity<KsLavaComponent> entity, ref StepTriggerAttemptEvent args)
    {
        args.Continue = true;
    }

    private void OnStepTriggered(Entity<KsLavaComponent> entity, ref StepTriggeredOffEvent args)
    {
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
