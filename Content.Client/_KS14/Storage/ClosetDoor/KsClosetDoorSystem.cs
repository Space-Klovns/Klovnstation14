using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Client.Lock.Visualizers;
using Content.Client.Storage.Visualizers;
using Content.Shared._KS14;
using Content.Shared.Storage;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Reflection;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Storage.ClosetDoor;

/// <summary>
///     Swings closet doors open and shut, porting tgstation's <c>/obj/structure/closet/proc/animate_door</c>. This
///         system keeps each closet's swing; <see cref="KsClosetDoorOverlay"/> draws it.
/// </summary>
/// <remarks>
///     The swing runs on real time, as tgstation's runs on client time, so it doesn't stutter or replay with
///         prediction. Only a change of <see cref="StorageVisuals.Open"/> starts one: the value a closet first
///         shows up with is snapped to, as tgstation does for closets that are open at round start.
///     While the door is drawn by the overlay, the sprite's own shut door and everything mounted on it
///         (<see cref="KsClosetDoorVisualsComponent.Layers"/>) are hidden, and this system alone shows them again once
///         the door is shut, in the same frame the overlay stops drawing it.
/// </remarks>
public sealed partial class KsClosetDoorSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IReflectionManager _reflectionManager = default!;
    [Dependency] private AppearanceSystem _appearanceSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private KsClosetDoorTreeSystem _treeSystem = default!;

    [Dependency] private EntityQuery<KsClosetDoorVisualsComponent> _doorQuery = default!;
    [Dependency] private EntityQuery<KsClosetDoorAnimatingComponent> _animatingQuery = default!;
    [Dependency] private EntityQuery<KsClosetContentFacingComponent> _facingQuery = default!;
    [Dependency] private EntityQuery<EntityStorageVisualsComponent> _storageVisualsQuery = default!;

    /// <summary>
    ///     Past this angle the door's back faces the viewer, and it is drawn behind whatever stands in front of the
    ///         closet rather than over it.
    /// </summary>
    public const float BackFacingAngle = 90f;

    /// <summary>
    ///     The real time as of this frame's <see cref="FrameUpdate"/>. Swings are timed by it rather than by the live
    ///         clock, so the overlay, drawing later in the frame, sees the same swing this system just decided on: a
    ///         swing that ended between the two would be drawn at no angle while the sprite's door was still hidden,
    ///         and the closet would flicker doorless for a frame.
    /// </summary>
    private TimeSpan _frameRealTime;

    public override void Initialize()
    {
        base.Initialize();

        _transformSystem.OnGlobalMoveEvent += OnGlobalMove;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _transformSystem.OnGlobalMoveEvent -= OnGlobalMove;
    }

    // After the visualizers that show the shut door and what is mounted on it as soon as the storage closes: the
    //     storage one (the door), the generic one (the paper label) and the lock one (the lock). What they show on a
    //     shut closet is what goes on the door; while the door is drawn, it comes off the sprite.
    [SubscribeLocalEvent(after: [typeof(EntityStorageVisualizerSystem), typeof(GenericVisualizerSystem), typeof(LockVisualizerSystem)])]
    private void OnAppearanceChange(Entity<KsClosetDoorVisualsComponent> entity, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null ||
            !_appearanceSystem.TryGetData<bool>(entity.Owner, StorageVisuals.Open, out var open, args.Component))
            return;

        if (entity.Comp.Open != open)
        {
            var swings = entity.Comp.Open != null && entity.Comp.Duration > TimeSpan.Zero;
            entity.Comp.Open = open;

            if (swings)
            {
                // A swing that is reversed halfway starts over from the other end, as tgstation's does.
                entity.Comp.AnimationStart = _gameTiming.RealTime;
                EnsureComp<KsClosetDoorAnimatingComponent>(entity.Owner);
            }

            SetDoorDrawn(entity, swings || (open && entity.Comp.Duration > TimeSpan.Zero));
        }

        var spriteEntity = new Entity<SpriteComponent?>(entity.Owner, args.Sprite);
        if (!open)
            RecordShutLayers(entity, spriteEntity);

        if (!entity.Comp.DoorDrawn)
            return;

        foreach (var layerId in entity.Comp.Layers.Keys)
        {
            if (!TryGetMountedLayer(spriteEntity, layerId, out var layer))
                continue;

            _spriteSystem.LayerSetVisible(layer, false);
            entity.Comp.HiddenLayers.Add(layerId);
        }

        // tgstation draws no shut door until the swing has finished.
        if (!open && _spriteSystem.TryGetLayer(spriteEntity, StorageVisualLayers.Door, out var doorLayer, logMissing: false))
        {
            _spriteSystem.LayerSetVisible(doorLayer, false);
            entity.Comp.DoorLayerHidden = true;
        }
    }

    /// <summary>
    ///     Notes which mounted layers the visualizers show on the shut closet. A layer this system hid says nothing
    ///         about that, so it keeps what was noted before.
    /// </summary>
    private void RecordShutLayers(Entity<KsClosetDoorVisualsComponent> entity, Entity<SpriteComponent?> spriteEntity)
    {
        foreach (var layerId in entity.Comp.Layers.Keys)
        {
            if (!TryGetMountedLayer(spriteEntity, layerId, out var layer) ||
                (!layer.Visible && entity.Comp.HiddenLayers.Contains(layerId)))
                continue;

            entity.Comp.ShutLayerVisibility[layerId] = layer.Visible;
            entity.Comp.HiddenLayers.Remove(layerId);
        }
    }

    /// <summary>
    ///     Puts back on the sprite everything this system took off it, as it was on the shut closet.
    /// </summary>
    private void RestoreShutLayers(Entity<KsClosetDoorVisualsComponent> entity, Entity<SpriteComponent?> spriteEntity)
    {
        foreach (var layerId in entity.Comp.HiddenLayers)
        {
            if (TryGetMountedLayer(spriteEntity, layerId, out var layer))
                _spriteSystem.LayerSetVisible(layer, entity.Comp.ShutLayerVisibility.GetValueOrDefault(layerId));
        }

        entity.Comp.HiddenLayers.Clear();

        if (!entity.Comp.DoorLayerHidden)
            return;

        entity.Comp.DoorLayerHidden = false;
        if (_spriteSystem.TryGetLayer(spriteEntity, StorageVisualLayers.Door, out var doorLayer, logMissing: false))
        {
            var hasShutDoor = _storageVisualsQuery.TryComp(entity.Owner, out var storageVisualsComponent) &&
                storageVisualsComponent.StateDoorClosed != null;
            _spriteSystem.LayerSetVisible(doorLayer, hasShutDoor);
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        _frameRealTime = _gameTiming.RealTime;
        var now = _frameRealTime;
        var animatingEnumerator = EntityQueryEnumerator<KsClosetDoorAnimatingComponent, KsClosetDoorVisualsComponent, SpriteComponent>();
        while (animatingEnumerator.MoveNext(out var uid, out var animatingComponent, out var doorComponent, out var spriteComponent))
        {
            if (animatingComponent.LifeStage > ComponentLifeStage.Running ||
                now < doorComponent.AnimationStart + doorComponent.Duration)
                continue;

            RemCompDeferred(uid, animatingComponent);
            SetDoorDrawn((uid, doorComponent), doorComponent.Open == true);

            // Shown here rather than by the visualizers on their next update, which may be a frame away: the overlay
            //     stops drawing the door this frame, and the closet would flicker doorless in between.
            if (doorComponent.Open == false)
                RestoreShutLayers((uid, doorComponent), (uid, spriteComponent));
        }
    }

    /// <summary>
    ///     Notes which way whatever goes into a closet was facing, since going in zeroes its rotation.
    /// </summary>
    private void OnGlobalMove(ref MoveEvent args)
    {
        if (!args.ParentChanged)
            return;

        if (_doorQuery.HasComp(args.NewPosition.EntityId))
        {
            var oldParentUid = args.OldPosition.EntityId;
            if (!EntityManager.TransformQuery.HasComp(oldParentUid))
                return;

            var facingComponent = EnsureComp<KsClosetContentFacingComponent>(args.Sender);
            facingComponent.WorldRotation = _transformSystem.GetWorldRotation(oldParentUid) + args.OldRotation;
        }
        else if (_facingQuery.HasComp(args.Sender))
            RemCompDeferred<KsClosetContentFacingComponent>(args.Sender);
    }

    /// <summary>
    ///     The world rotation to draw something inside a closet with: the way it faced before it went in.
    /// </summary>
    public Angle GetContentWorldRotation(EntityUid uid)
    {
        return _facingQuery.TryComp(uid, out var facingComponent) && facingComponent.LifeStage <= ComponentLifeStage.Running
            ? facingComponent.WorldRotation
            : _transformSystem.GetWorldRotation(uid);
    }

    /// <summary>
    ///     A layer named in <see cref="KsClosetDoorVisualsComponent.Layers"/>, by its enum or string key.
    /// </summary>
    public bool TryGetMountedLayer(Entity<SpriteComponent?> spriteEntity, string layerId, [NotNullWhen(true)] out SpriteComponent.Layer? layer)
    {
        var enumKey = KsEnumHelpers.ParseKey(layerId, out var isEnum, _reflectionManager);
        return isEnum
            ? _spriteSystem.TryGetLayer(spriteEntity, enumKey!, out layer, logMissing: false)
            : _spriteSystem.TryGetLayer(spriteEntity, layerId, out layer, logMissing: false);
    }

    /// <summary>
    ///     Whether the door is mid-swing. A finished swing's marker is removed at the end of the tick, so it is
    ///         only counted while it is still running.
    /// </summary>
    public bool IsAnimating(EntityUid uid)
    {
        return _animatingQuery.TryComp(uid, out var animatingComponent) &&
            animatingComponent.LifeStage <= ComponentLifeStage.Running;
    }

    /// <summary>
    ///     How far the door is swung open as of this frame, in degrees: 0 is shut.
    /// </summary>
    /// <remarks>
    ///     A swing that started this frame, after <see cref="FrameUpdate"/>, is at its start until the next one.
    /// </remarks>
    public float GetDoorAngle(Entity<KsClosetDoorVisualsComponent> entity)
    {
        var open = entity.Comp.Open == true;
        if (!IsAnimating(entity.Owner))
            return open ? entity.Comp.MaxAngle : 0f;

        var progress = (float)((_frameRealTime - entity.Comp.AnimationStart) / entity.Comp.Duration);
        progress = Math.Clamp(progress, 0f, 1f);

        return entity.Comp.MaxAngle * (open ? progress : 1f - progress);
    }

    /// <summary>
    ///     Whether the closet is swinging shut, so its contents are still drawn inside it.
    /// </summary>
    public bool IsClosing(Entity<KsClosetDoorVisualsComponent> entity)
    {
        return entity.Comp.Open == false && IsAnimating(entity.Owner);
    }

    /// <summary>
    ///     The transform that swings a door, drawn centred on the origin in metres, to <paramref name="angleDegrees"/>
    ///         about a vertical hinge <paramref name="hingeX"/> metres from its centre.
    /// </summary>
    /// <remarks>
    ///     A port of tgstation's <c>get_door_transform</c>:
    ///     <code>
    ///     door_matrix.Translate(-door_hinge_x, 0)
    ///     door_matrix.Multiply(matrix(cos(angle), 0, 0, -sin(angle) * door_anim_squish, 1, 0))
    ///     door_matrix.Translate(door_hinge_x, 0)
    ///     </code>
    ///     BYOND's <c>matrix(a, b, c, d, e, f)</c> maps <c>x' = ax + by + c</c> and <c>y' = dx + ey + f</c>, which
    ///         is <c>new Matrix3x2(a, d, b, e, c, f)</c> here. So the door is squeezed to <c>cos</c> of its width,
    ///         and sheared vertically in proportion to the distance from the hinge, faking perspective. Past 90
    ///         degrees <c>cos</c> is negative, mirroring the door, which is when its back is drawn. Both y axes
    ///         point up, so the far edge drops on screen as it does in tgstation.
    ///     Composed left to right, since <see cref="Matrix3x2"/> transforms row vectors.
    /// </remarks>
    public static Matrix3x2 GetDoorTransform(float angleDegrees, float hingeX, float squish)
    {
        var angle = MathHelper.DegreesToRadians(angleDegrees);
        var swing = new Matrix3x2(MathF.Cos(angle), -MathF.Sin(angle) * squish, 0f, 1f, 0f, 0f);

        return Matrix3x2.CreateTranslation(-hingeX, 0f) * swing * Matrix3x2.CreateTranslation(hingeX, 0f);
    }

    /// <summary>
    ///     <see cref="GetDoorTransform(float, float, float)"/> for a closet's door, swung to <paramref name="angleDegrees"/>.
    /// </summary>
    public static Matrix3x2 GetDoorTransform(KsClosetDoorVisualsComponent doorComponent, float angleDegrees)
    {
        return GetDoorTransform(angleDegrees, doorComponent.HingeX / EyeManager.PixelsPerMeter, doorComponent.Squish);
    }

    private void SetDoorDrawn(Entity<KsClosetDoorVisualsComponent> entity, bool doorDrawn)
    {
        if (entity.Comp.DoorDrawn == doorDrawn)
            return;

        entity.Comp.DoorDrawn = doorDrawn;
        _treeSystem.QueueTreeUpdate(entity);
    }
}
