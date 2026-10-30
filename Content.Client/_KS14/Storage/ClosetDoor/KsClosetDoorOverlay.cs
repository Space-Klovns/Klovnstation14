using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Client._KS14.Graphics;
using Content.Client.Storage.Visualizers;
using Content.Shared.Storage.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.ComponentTrees;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Timing;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._KS14.Storage.ClosetDoor;

/// <summary>
///     Draws closet doors that aren't shut, skewed by <see cref="KsClosetDoorSystem.GetDoorTransform(float, float, float)"/>,
///         with the layers mounted on them (<see cref="KsClosetDoorVisualsComponent.Layers"/>).
///     As in tgstation, a door swung less than 90 degrees shows its front and is drawn over mobs
///         (<see cref="KsClosetDoorFrontOverlay"/>). Past that it shows its back and is drawn just over the closet,
///         under whatever stands in front of it (<see cref="KsClosetDoorBackOverlay"/>). The back pass also draws a
///         closing closet's contents, clipped to its doorway, until the door has covered them.
/// </summary>
/// <remarks>
///     One subclass per pass, since <see cref="IOverlayManager"/> keys overlays by type. Each pass is one draw in
///         the sprite order, so it y-sorts its own doors.
/// </remarks>
public abstract partial class KsClosetDoorOverlay : Overlay
{
    [Dependency] private EntityManager _entityManager = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private KsClosetDoorSystem _doorSystem = default!;

    [Dependency] private EntityQuery<KsClosetDoorTreeComponent> _treeQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<EntityStorageVisualsComponent> _storageVisualsQuery = default!;
    [Dependency] private EntityQuery<EntityStorageComponent> _entityStorageQuery = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    /// <summary>
    ///     Whether doors and what's mounted on them are drawn in whole sprite pixels, through
    ///         <c>skew_pixel_snap.swsl</c>. Follows <see cref="Content.Shared._KS14.CCVar.KsCCVars.ClosetDoorPixelSnap"/>.
    /// </summary>
    public bool PixelSnap;

    /// <summary>
    ///     Whether a closing closet's doorway and contents are tinted, to see the clip at work. Follows
    ///         <see cref="Content.Shared._KS14.CCVar.KsCCVars.ClosetDoorDebugMask"/>.
    /// </summary>
    public bool DebugMask;

    private static readonly Color DebugMaskColor = Color.Magenta.WithAlpha(0.35f);
    private static readonly Color DebugContentsColor = Color.Lime;

    private List<ComponentTreeEntry<KsClosetDoorVisualsComponent>> _entries = []; // not readonly: the tree query takes it by ref
    private List<Entity<MapGridComponent>> _grids = [];
    private readonly List<DrawEntry> _drawEntries = [];

    /// <summary>
    ///     Instances of the snapping shaders, one per draw: their uniforms differ per draw, and a uniform is read
    ///         when the batch is drawn, not when it is set. Reused from the start every frame.
    /// </summary>
    private readonly ShaderPool _snapShaders;

    private readonly ShaderPool _snapUnshadedShaders;

    /// <param name="snapShader">The lit snapping shader, <c>KsSkewPixelSnap</c>.</param>
    /// <param name="snapUnshadedShader">The unshaded one, <c>KsSkewPixelSnapUnshaded</c>.</param>
    protected KsClosetDoorOverlay(int zIndex, ShaderPrototype snapShader, ShaderPrototype snapUnshadedShader)
    {
        ZIndex = zIndex;
        _snapShaders = new ShaderPool(snapShader);
        _snapUnshadedShaders = new ShaderPool(snapUnshadedShader);
    }

    /// <summary>
    ///     Whether this pass draws anything for a door swung to <paramref name="angle"/>.
    /// </summary>
    protected abstract bool DrawsAt(float angle, bool closing);

    /// <summary>
    ///     Draws one closet's part of this pass.
    /// </summary>
    protected abstract void DrawCloset(in DrawContext context, in DrawEntry entry);

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        _snapShaders.Rewind();
        _snapUnshadedShaders.Rewind();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return args.Viewport.Eye != null &&
            _entityManager.Count<KsClosetDoorVisualsComponent>() > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var bounds = args.WorldBounds;

        _grids.Clear();
        _mapSystem.FindGridsIntersecting(args.MapId, bounds, ref _grids, approx: true);
        if (_grids.Count == 0)
            return;

        var worldHandle = args.WorldHandle;
        var transformQuery = _entityManager.TransformQuery;
        var eye = args.Viewport.Eye!;
        var eyeRotation = eye.Rotation;

        var viewPosition = eye.Position.Position + eye.Offset;
        var viewScale = eye.Scale * args.Viewport.RenderScale * new Vector2(EyeManager.PixelsPerMeter, -EyeManager.PixelsPerMeter);
        var viewportSize = (Vector2)args.Viewport.Size;

        foreach (var grid in _grids)
        {
            if (!_treeQuery.TryComp(grid.Owner, out var treeComponent))
                continue;

            var (_, gridRotation, worldMatrix, invWorldMatrix) = _transformSystem.GetWorldPositionRotationMatrixWithInv(grid.Owner);

            _entries.Clear();
            treeComponent.Tree.QueryAabb(ref _entries,
                static (ref List<ComponentTreeEntry<KsClosetDoorVisualsComponent>> entries, in ComponentTreeEntry<KsClosetDoorVisualsComponent> entry) =>
                {
                    entries.Add(entry);
                    return true;
                },
                invWorldMatrix.TransformBox(bounds),
                approx: true);

            if (_entries.Count == 0)
                continue;

            var localEyeRotation = eyeRotation + gridRotation;

            _drawEntries.Clear();
            foreach (var treeEntry in _entries)
            {
                var doorEntity = new Entity<KsClosetDoorVisualsComponent>(treeEntry.Uid, treeEntry.Component);
                var angle = _doorSystem.GetDoorAngle(doorEntity);
                var closing = _doorSystem.IsClosing(doorEntity);
                if (!DrawsAt(angle, closing))
                    continue;

                if (!_spriteQuery.TryComp(doorEntity.Owner, out var spriteComponent) ||
                    !spriteComponent.Visible ||
                    !_storageVisualsQuery.TryComp(doorEntity.Owner, out var storageVisualsComponent) ||
                    !_spriteSystem.TryGetLayer((doorEntity.Owner, spriteComponent), StorageVisualLayers.Base, out var baseLayer, logMissing: false))
                    continue;

                var position = transformQuery.GetComponent(doorEntity.Owner).LocalPosition;

                // As a no-rotation sprite is placed: upright on screen, whatever the grid and eye do. Its layers'
                //     own matrices, and the door's swing, go in front of this.
                var spriteMatrix = spriteComponent.LocalMatrix * Matrix3Helpers.CreateTransform(position, -localEyeRotation);

                _drawEntries.Add(new DrawEntry(
                    doorEntity,
                    spriteComponent,
                    storageVisualsComponent,
                    baseLayer,
                    spriteMatrix,
                    angle,
                    closing,
                    localEyeRotation.RotateVec(position).Y));
            }

            if (_drawEntries.Count == 0)
                continue;

            // Back to front, the way the sprite renderer y-sorts: whatever is lower on screen is drawn later.
            _drawEntries.Sort(static (a, b) =>
            {
                var order = b.ScreenHeight.CompareTo(a.ScreenHeight);
                return order != 0 ? order : a.Door.Owner.Id.CompareTo(b.Door.Owner.Id);
            });

            // Snapped the same way the engine snaps a grid's sprites, so doors stay fixed to their closets.
            var snappedWorldMatrix = worldMatrix;
            snappedWorldMatrix.Translation += KsPixelSnap.GetOffset(worldMatrix.Translation, viewPosition, eyeRotation, viewScale, viewportSize);

            var context = new DrawContext(worldHandle, snappedWorldMatrix, eyeRotation);
            foreach (var entry in _drawEntries)
                DrawCloset(context, entry);
        }

        _drawEntries.Clear();

        worldHandle.UseShader(null);
        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>
    ///     Draws one face of a closet's door, swung to the entry's angle: <paramref name="stateName"/> from the
    ///         closet's RSI, then each mounted layer on <paramref name="face"/> that was shown on the shut closet.
    /// </summary>
    protected void DrawDoor(in DrawContext context, in DrawEntry entry, string? stateName, KsClosetDoorFace face)
    {
        var swing = KsClosetDoorSystem.GetDoorTransform(entry.Door.Comp, entry.Angle);

        if (TryGetTexture(entry, stateName, out var doorTexture))
        {
            DrawQuad(context,
                entry,
                doorTexture,
                entry.BaseLayer.LocalMatrix * swing,
                entry.Sprite.Color * entry.BaseLayer.Color,
                unshaded: false,
                layerShader: null);
        }

        var spriteEntity = new Entity<SpriteComponent?>(entry.Door.Owner, entry.Sprite);
        foreach (var (layerId, layerFace) in entry.Door.Comp.Layers)
        {
            if ((layerFace != face && layerFace != KsClosetDoorFace.Both) ||
                !entry.Door.Comp.ShutLayerVisibility.GetValueOrDefault(layerId) ||
                !_doorSystem.TryGetMountedLayer(spriteEntity, layerId, out var layer) ||
                layer.Blank)
                continue;

            var texture = layer.ActualState?.GetFrame(RsiDirection.South, layer.AnimationFrame) ?? layer.Texture;
            if (texture == null)
                continue;

            DrawQuad(context,
                entry,
                texture,
                layer.LocalMatrix * swing,
                entry.Sprite.Color * layer.Color,
                unshaded: layer.ShaderPrototype == SpriteSystem.UnshadedId,
                layerShader: layer.Shader);
        }
    }

    /// <summary>
    ///     Draws a texture centred on the origin, placed in the closet's sprite by <paramref name="quadToSprite"/>.
    /// </summary>
    private void DrawQuad(
        in DrawContext context,
        in DrawEntry entry,
        Texture texture,
        Matrix3x2 quadToSprite,
        Color color,
        bool unshaded,
        ShaderInstance? layerShader)
    {
        var worldHandle = context.WorldHandle;
        worldHandle.SetTransform(quadToSprite * entry.SpriteMatrix * context.SnappedWorldMatrix);
        var quad = GetQuad(texture);

        // A layer with a shader of its own keeps it, unsnapped.
        if (layerShader != null)
        {
            worldHandle.UseShader(layerShader);
            worldHandle.DrawTextureRect(texture, quad, color);
            worldHandle.UseShader(null);
            return;
        }

        if (PixelSnap)
        {
            var shader = (unshaded ? _snapUnshadedShaders : _snapShaders).Next();
            shader.SetParameter("quadSize", (Vector2)texture.Size);
            shader.SetParameter("transform", new Vector4(quadToSprite.M11, quadToSprite.M12, quadToSprite.M21, quadToSprite.M22));
            shader.SetParameter("translation", quadToSprite.Translation * EyeManager.PixelsPerMeter);

            worldHandle.UseShader(shader);
            worldHandle.DrawTextureRect(texture, quad, color);
            worldHandle.UseShader(null);
            return;
        }

        // The engine's own unshaded sprite layers are drawn the same way: a negative colour tells the default shader
        //     to skip the light.
        worldHandle.UseShader(null);
        worldHandle.DrawTextureRect(texture, quad, unshaded ? new Color(new Vector4(-1f) - color.RGBA) : color);
    }

    /// <summary>
    ///     Draws a closing closet's contents, clipped to its doorway: a port of tgstation's
    ///         <c>/obj/effect/appearance_clone/closet_item</c>, masked by the shut door's alpha. Without it, everything
    ///         inside would vanish the moment the closet started to close, while its doorway was still open.
    /// </summary>
    /// <remarks>
    ///     The contents' layers are drawn here one by one, all through the stencil shader, rather than by
    ///         <c>SpriteSystem.RenderSprite</c>: that switches to each layer's own shader and back to none, so every
    ///         layer after the first that has one would draw unclipped. Layer shaders (displacement, unshaded) are
    ///         dropped for the swing's length instead.
    /// </remarks>
    protected void DrawContents(in DrawContext context, in DrawEntry entry, ShaderInstance maskShader, ShaderInstance clearShader, ShaderInstance drawShader)
    {
        if (!_entityStorageQuery.TryComp(entry.Door.Owner, out var entityStorageComponent) ||
            entityStorageComponent.Contents.ContainedEntities.Count == 0 ||
            !TryGetTexture(entry, entry.StorageVisuals.StateDoorClosed, out var maskTexture))
            return;

        var worldHandle = context.WorldHandle;
        var maskMatrix = entry.BaseLayer.LocalMatrix * entry.SpriteMatrix * context.SnappedWorldMatrix;
        var maskQuad = GetQuad(maskTexture);

        worldHandle.UseShader(maskShader);
        worldHandle.SetTransform(maskMatrix);
        worldHandle.DrawTextureRect(maskTexture, maskQuad);

        // The doorway the contents are clipped to, unshaded (a negative colour) so it shows in the dark too.
        if (DebugMask)
        {
            worldHandle.UseShader(null);
            worldHandle.DrawTextureRect(maskTexture, maskQuad, new Color(new Vector4(-1f) - DebugMaskColor.RGBA));
        }

        var contentsTint = DebugMask ? DebugContentsColor : Color.White;

        // Where the closet stands, in the snapped frame its own sprite is drawn in.
        var closetPosition = Vector2.Transform(_entityManager.TransformQuery.GetComponent(entry.Door.Owner).LocalPosition, context.SnappedWorldMatrix);

        worldHandle.UseShader(drawShader);
        foreach (var containedUid in entityStorageComponent.Contents.ContainedEntities)
        {
            if (!_spriteQuery.TryComp(containedUid, out var containedSpriteComponent) || !containedSpriteComponent.Visible)
                continue;

            DrawSpriteLayers(worldHandle, containedSpriteComponent, context.EyeRotation, _doorSystem.GetContentWorldRotation(containedUid), closetPosition, contentsTint);
        }

        worldHandle.UseShader(clearShader);
        worldHandle.SetTransform(maskMatrix);
        worldHandle.DrawTextureRect(maskTexture, maskQuad);
        worldHandle.UseShader(null);
    }

    /// <summary>
    ///     Draws a sprite's layers where the engine's sprite renderer puts them, with whatever shader is in use.
    ///         Mirrors <c>SpriteSystem.Render</c> and <c>RenderLayer</c>, without layer shaders or post-shaders.
    /// </summary>
    private static void DrawSpriteLayers(
        DrawingHandleWorld worldHandle,
        SpriteComponent spriteComponent,
        Angle eyeRotation,
        Angle worldRotation,
        Vector2 worldPosition,
        Color tint)
    {
        var angle = (worldRotation + eyeRotation).Reduced().FlipPositive(); // on screen; picks 4- and 8-directional states
        var cardinal = spriteComponent is { NoRotation: false, SnapCardinals: true } ? angle.RoundToCardinalAngle() : Angle.Zero;

        var entityMatrix = Matrix3Helpers.CreateTransform(worldPosition, spriteComponent.NoRotation ? -eyeRotation : worldRotation - cardinal);
        var spriteMatrix = spriteComponent.LocalMatrix * entityMatrix;
        Direction? overrideDirection = spriteComponent.EnableDirectionOverride ? spriteComponent.DirectionOverride : null;

        foreach (var spriteLayer in spriteComponent.AllLayers)
        {
            if (spriteLayer is not SpriteComponent.Layer layer ||
                !layer.Visible ||
                layer.Blank ||
                layer.CopyToShaderParameters != null)
                continue;

            var state = layer.ActualState;
            var direction = state == null ? RsiDirection.South : SpriteComponent.Layer.GetDirection(state.RsiDirections, angle);
            layer.GetLayerDrawMatrix(direction, out var layerMatrix);

            if (overrideDirection != null && state != null)
                direction = overrideDirection.Value.Convert(state.RsiDirections);
            direction = direction.OffsetRsiDir(layer.DirOffset);

            var texture = state?.GetFrame(direction, layer.AnimationFrame) ?? layer.Texture;
            if (texture == null)
                continue;

            worldHandle.SetTransform(layerMatrix * spriteMatrix);
            worldHandle.DrawTextureRect(texture, GetQuad(texture), spriteComponent.Color * layer.Color * tint);
        }
    }

    private static Box2 GetQuad(Texture texture)
    {
        var size = texture.Size / (float)EyeManager.PixelsPerMeter;
        return Box2.FromDimensions(size / -2f, size);
    }

    /// <summary>
    ///     A state of the closet's own RSI, on the frame its base layer is showing, so an animated door keeps time
    ///         with the closet it belongs to.
    /// </summary>
    private static bool TryGetTexture(in DrawEntry entry, string? stateName, [NotNullWhen(true)] out Texture? texture)
    {
        texture = null;
        if (stateName == null ||
            entry.Sprite.BaseRSI is not { } rsi ||
            !rsi.TryGetState(stateName, out var state))
            return false;

        var frame = Math.Min(entry.BaseLayer.AnimationFrame, state.DelayCount - 1);
        texture = state.GetFrame(RsiDirection.South, Math.Max(frame, 0));
        return true;
    }

    protected override void DisposeBehavior()
    {
        _snapShaders.Dispose();
        _snapUnshadedShaders.Dispose();

        base.DisposeBehavior();
    }

    protected readonly record struct DrawContext(
        DrawingHandleWorld WorldHandle,
        Matrix3x2 SnappedWorldMatrix,
        Angle EyeRotation);

    /// <param name="SpriteMatrix">Places the closet's sprite, after its layers' own matrices, in the grid's frame.</param>
    protected readonly record struct DrawEntry(
        Entity<KsClosetDoorVisualsComponent> Door,
        SpriteComponent Sprite,
        EntityStorageVisualsComponent StorageVisuals,
        SpriteComponent.Layer BaseLayer,
        Matrix3x2 SpriteMatrix,
        float Angle,
        bool Closing,
        float ScreenHeight);

    /// <summary>
    ///     Unique instances of one shader, handed out one per draw and all taken back at once.
    /// </summary>
    private sealed class ShaderPool(ShaderPrototype shaderPrototype) : IDisposable
    {
        private readonly List<ShaderInstance> _instances = [];
        private int _next;

        public ShaderInstance Next()
        {
            if (_next == _instances.Count)
                _instances.Add(shaderPrototype.InstanceUnique());

            return _instances[_next++];
        }

        public void Rewind()
        {
            _next = 0;
        }

        public void Dispose()
        {
            foreach (var instance in _instances)
                instance.Dispose();

            _instances.Clear();
            _next = 0;
        }
    }
}

/// <summary>
///     The door's front, while it's swung less than 90 degrees: drawn over mobs, as tgstation's
///         <c>ABOVE_MOB_LAYER</c>, so it swings out over whoever is opening it.
/// </summary>
public sealed partial class KsClosetDoorFrontOverlay(ShaderPrototype snapShader, ShaderPrototype snapUnshadedShader)
    : KsClosetDoorOverlay((int)DrawDepth.OverMobs, snapShader, snapUnshadedShader)
{
    protected override bool DrawsAt(float angle, bool closing)
    {
        return angle > 0f && angle < KsClosetDoorSystem.BackFacingAngle;
    }

    protected override void DrawCloset(in DrawContext context, in DrawEntry entry)
    {
        DrawDoor(context, entry, entry.StorageVisuals.StateDoorClosed, KsClosetDoorFace.Front);
    }
}

/// <summary>
///     The door's back, from 90 degrees on, and a closing closet's contents: drawn just over the closet and under
///         everything in front of it, as tgstation draws them on the closet itself.
/// </summary>
public sealed partial class KsClosetDoorBackOverlay(
    ShaderPrototype snapShader,
    ShaderPrototype snapUnshadedShader,
    ShaderInstance maskShader,
    ShaderInstance clearShader,
    ShaderInstance drawShader)
    : KsClosetDoorOverlay((int)DrawDepth.SmallObjects, snapShader, snapUnshadedShader)
{
    protected override bool DrawsAt(float angle, bool closing)
    {
        return closing || angle >= KsClosetDoorSystem.BackFacingAngle;
    }

    protected override void DrawCloset(in DrawContext context, in DrawEntry entry)
    {
        if (entry.Closing)
            DrawContents(context, entry, maskShader, clearShader, drawShader);

        if (entry.Angle >= KsClosetDoorSystem.BackFacingAngle)
            DrawDoor(context, entry, entry.StorageVisuals.StateBaseClosed + KsClosetDoorVisualsComponent.BackStateSuffix, KsClosetDoorFace.Back);
    }
}
