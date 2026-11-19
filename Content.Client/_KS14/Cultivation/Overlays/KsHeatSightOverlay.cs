using System.Numerics;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Cultivation.Overlays;

/// <summary>
///     Draws every living creature within range that the viewer can't see directly as a warm, glowing silhouette, above
///         the line-of-sight shadow, so walls don't hide them.
/// </summary>
public sealed partial class KsHeatSightOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly Color Heat = Color.FromHex("#ff8a3c");

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private readonly SpriteSystem _spriteSystem;
    private readonly SharedTransformSystem _transformSystem;
    private readonly EntityLookupSystem _lookupSystem;
    private readonly ExamineSystemShared _examineSystem;
    private readonly MobStateSystem _mobStateSystem;
    private readonly ShaderInstance _shader;
    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public KsHeatSightOverlay()
    {
        IoCManager.InjectDependencies(this);
        _spriteSystem = _entityManager.System<SpriteSystem>();
        _transformSystem = _entityManager.System<SharedTransformSystem>();
        _lookupSystem = _entityManager.System<EntityLookupSystem>();
        _examineSystem = _entityManager.System<ExamineSystemShared>();
        _mobStateSystem = _entityManager.System<MobStateSystem>();
        _shader = _prototypeManager.Index(UnshadedShader).Instance();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_playerManager.LocalEntity is not { } viewerUid
            || !_entityManager.TryGetComponent<KsHeatSightComponent>(viewerUid, out var heatSight))
            return;

        var viewerPosition = _transformSystem.GetMapCoordinates(viewerUid);
        if (viewerPosition.MapId != args.MapId)
            return;

        var handle = args.WorldHandle;
        var eyeRotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        // Breathe a little, like heat shimmer.
        var pulse = 0.55f + 0.1f * MathF.Sin((float)(DateTime.Now.TimeOfDay.TotalSeconds * 3.0));

        _mobs.Clear();
        _lookupSystem.GetEntitiesInRange(viewerPosition, heatSight.Range, _mobs);
        handle.UseShader(_shader);
        foreach (var mob in _mobs)
        {
            var uid = mob.Owner;
            if (uid == viewerUid
                || _mobStateSystem.IsDead(uid, mob.Comp)
                || !_entityManager.TryGetComponent<SpriteComponent>(uid, out var sprite)
                || _examineSystem.InRangeUnOccluded(viewerUid, uid, heatSight.Range))
                continue;

            var (position, rotation) = _transformSystem.GetWorldPositionRotation(uid);
            handle.Modulate = Heat.WithAlpha(pulse);
            _spriteSystem.RenderSprite((uid, sprite), handle, eyeRotation, rotation, position);
        }

        handle.Modulate = Color.White;
        handle.UseShader(null);
    }
}
