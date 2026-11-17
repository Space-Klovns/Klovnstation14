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
    [Dependency] private EntityLookupSystem _lookupSystem = default!;
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

    /// <summary>
    ///     Whether an open door is hidden behind the closets drawn in front of it. Follows
    ///         <see cref="Content.Shared._KS14.CCVar.KsCCVars.ClosetDoorOcclusionMask"/>.
    /// </summary>
    public bool OcclusionMask;

    /// <summary>
    ///     How far above its own bounds a closet's sprite can reach: tgstation's closets stand half a tile taller than
    ///         the tile they're on.
    /// </summary>
    private const float OccluderReach = 0.5f;

    private static readonly Color DebugMaskColor = Color.Magenta.WithAlpha(0.35f);
    private static readonly Color DebugContentsColor = Color.Lime;

    private List<ComponentTreeEntry<KsClosetDoorVisualsComponent>> _entries = []; // not readonly: the tree query takes it by ref
    private List<Entity<MapGridComponent>> _grids = [];
    private readonly List<DrawEntry> _drawEntries = [];
    private readonly HashSet<Entity<KsClosetDoorVisualsComponent>> _occluderCandidates = [];
    private readonly List<Occluder> _occluders = [];

    /// <summary>
    ///     Instances of the snapping shaders, one per draw: their uniforms differ per draw, and a uniform is read
    ///         when the batch is drawn, not when it is set. Reused from the start every frame.
    /// </summary>
    private readonly ShaderPool _snapShaders;

    private readonly ShaderPool _snapUnshadedShaders;

    private readonly KsClosetDoorOcclusionShaders? _occlusionShaders;

    /// <summary>
    ///     The snapping shader drawn only outside the occlusion mask, for doors partly behind other closets.
    /// </summary>
    private readonly ShaderPool? _snapOccludedShaders;

    /// <param name="snapShader">The lit snapping shader, <c>KsSkewPixelSnap</c>.</param>
    /// <param name="snapUnshadedShader">The unshaded one, <c>KsSkewPixelSnapUnshaded</c>.</param>
    /// <param name="occlusionShaders">For a pass that hides doors behind the closets in front of them.</param>
    protected KsClosetDoorOverlay(
        int zIndex,
        ShaderPrototype snapShader,
        ShaderPrototype snapUnshadedShader,
        KsClosetDoorOcclusionShaders? occlusionShaders = null)
    {
        ZIndex = zIndex;
        _snapShaders = new ShaderPool(snapShader);
        _snapUnshadedShaders = new ShaderPool(snapUnshadedShader);

        _occlusionShaders = occlusionShaders;
        if (occlusionShaders != null)
            _snapOccludedShaders = new ShaderPool(occlusionShaders.SnapDraw);
    }

    /// <summary>
    ///     Whether this pass draws anything for a door swung to <paramref name="angle"/>.
    /// </summary>
    protected abstract bool DrawsAt(float angle, bool animating, bool closing);

    /// <summary>
    ///     Draws one closet's part of this pass.
    /// </summary>
    protected abstract void DrawCloset(in DrawContext context, in DrawEntry entry);

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        _snapShaders.Rewind();
        _snapUnshadedShaders.Rewind();
        _snapOccludedShaders?.Rewind();
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

        var worldHandle = args.WorldHandle;
        var eye = args.Viewport.Eye!;
        var view = new ViewData(
            eye.Position.Position + eye.Offset,
            eye.Rotation,
            eye.Scale * args.Viewport.RenderScale * new Vector2(EyeManager.PixelsPerMeter, -EyeManager.PixelsPerMeter),
            (Vector2)args.Viewport.Size);

        foreach (var grid in _grids)
            DrawTree(worldHandle, bounds, view, grid.Owner, pixelSnap: true);

        // Closets off any grid, drifting in space, are in the map's own tree. The engine doesn't snap those sprites.
        DrawTree(worldHandle, bounds, view, args.MapUid, pixelSnap: false);

        _drawEntries.Clear();

        worldHandle.UseShader(null);
        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>
    ///     Draws this pass for the closets in one grid's or map's tree.
    /// </summary>
    private void DrawTree(DrawingHandleWorld worldHandle, Box2Rotated bounds, in ViewData view, EntityUid treeOwnerUid, bool pixelSnap)
    {
        if (!_treeQuery.TryComp(treeOwnerUid, out var treeComponent))
            return;

        var (_, treeRotation, worldMatrix, invWorldMatrix) = _transformSystem.GetWorldPositionRotationMatrixWithInv(treeOwnerUid);

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
            return;

        var transformQuery = _entityManager.TransformQuery;
        var localEyeRotation = view.Rotation + treeRotation;

        _drawEntries.Clear();
        foreach (var treeEntry in _entries)
        {
            var doorEntity = new Entity<KsClosetDoorVisualsComponent>(treeEntry.Uid, treeEntry.Component);
            var angle = _doorSystem.GetDoorAngle(doorEntity);
            var closing = _doorSystem.IsClosing(doorEntity);
            if (!DrawsAt(angle, _doorSystem.IsAnimating(doorEntity.Owner), closing))
                continue;

            if (!_spriteQuery.TryComp(doorEntity.Owner, out var spriteComponent) ||
                !spriteComponent.Visible ||
                !_storageVisualsQuery.TryComp(doorEntity.Owner, out var storageVisualsComponent) ||
                !_spriteSystem.TryGetLayer((doorEntity.Owner, spriteComponent), StorageVisualLayers.Base, out var baseLayer, logMissing: false))
                continue;

            var position = GetTreePosition(transformQuery.GetComponent(doorEntity.Owner), treeOwnerUid, invWorldMatrix);

            // As a no-rotation sprite is placed: upright on screen, whatever the grid and eye do. Its layers'
            //     own matrices, and the door's swing, go in front of this.
            var spriteMatrix = spriteComponent.LocalMatrix * Matrix3Helpers.CreateTransform(position, -localEyeRotation);

            _drawEntries.Add(new DrawEntry(
                doorEntity,
                spriteComponent,
                storageVisualsComponent,
                baseLayer,
                position,
                spriteMatrix,
                angle,
                closing,
                GetScreenHeight(position, localEyeRotation)));
        }

        if (_drawEntries.Count == 0)
            return;

        // Back to front, the way the sprite renderer y-sorts: whatever is lower on screen is drawn later.
        _drawEntries.Sort(static (a, b) =>
        {
            var order = b.ScreenHeight.CompareTo(a.ScreenHeight);
            return order != 0 ? order : a.Door.Owner.Id.CompareTo(b.Door.Owner.Id);
        });

        // Snapped the same way the engine snaps a grid's sprites, so doors stay fixed to their closets.
        var snappedWorldMatrix = worldMatrix;
        if (pixelSnap)
            snappedWorldMatrix.Translation += KsPixelSnap.GetOffset(worldMatrix.Translation, view.Position, view.Rotation, view.Scale, view.Size);

        var context = new DrawContext(
            worldHandle,
            treeOwnerUid,
            snappedWorldMatrix,
            invWorldMatrix,
            view.Rotation,
            localEyeRotation,
            bounds);
        foreach (var entry in _drawEntries)
            DrawCloset(context, entry);
    }

    /// <summary>
    ///     Where an entity in a grid's (or map's) tree stands, in that grid's frame.
    /// </summary>
    private Vector2 GetTreePosition(TransformComponent transformComponent, EntityUid treeOwnerUid, in Matrix3x2 invWorldMatrix)
    {
        return transformComponent.ParentUid == treeOwnerUid
            ? transformComponent.LocalPosition
            : Vector2.Transform(_transformSystem.GetWorldPosition(transformComponent), invWorldMatrix);
    }

    /// <summary>
    ///     How high on screen something at <paramref name="position"/> in the grid's frame is, as the sprite renderer
    ///         y-sorts: whatever is lower is drawn later.
    /// </summary>
    private static float GetScreenHeight(Vector2 position, Angle localEyeRotation)
    {
        return localEyeRotation.RotateVec(position).Y;
    }

    /// <summary>
    ///     Draws one face of a closet's door, swung to the entry's angle: <paramref name="stateName"/> from the
    ///         closet's RSI, then each mounted layer on <paramref name="face"/> that was shown on the shut closet.
    /// </summary>
    /// <param name="occluded">Whether to draw only outside the mask <see cref="MaskOccluders"/> wrote.</param>
    protected void DrawDoor(in DrawContext context, in DrawEntry entry, string? stateName, KsClosetDoorFace face, bool occluded = false)
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
                layerShader: null,
                occluded);
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
                layerShader: layer.Shader,
                occluded);
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
        ShaderInstance? layerShader,
        bool occluded)
    {
        var worldHandle = context.WorldHandle;
        worldHandle.SetTransform(quadToSprite * entry.SpriteMatrix * context.SnappedWorldMatrix);
        var quad = GetQuad(texture);

        // A layer with a shader of its own keeps it, unsnapped and unmasked.
        if (layerShader != null)
        {
            worldHandle.UseShader(layerShader);
            worldHandle.DrawTextureRect(texture, quad, color);
            worldHandle.UseShader(null);
            return;
        }

        // Behind other closets the stencil-testing shaders are lit, so an unshaded layer is drawn with a negative
        //     colour, which tells any shader with the default wrapper to skip the light.
        occluded &= _occlusionShaders != null;
        var occludedColor = unshaded ? GetUnshadedColor(color) : color;

        if (PixelSnap)
        {
            var shader = (occluded ? _snapOccludedShaders! : unshaded ? _snapUnshadedShaders : _snapShaders).Next();
            shader.SetParameter("quadSize", (Vector2)texture.Size);
            shader.SetParameter("transform", new Vector4(quadToSprite.M11, quadToSprite.M12, quadToSprite.M21, quadToSprite.M22));
            shader.SetParameter("translation", quadToSprite.Translation * EyeManager.PixelsPerMeter);

            worldHandle.UseShader(shader);
            worldHandle.DrawTextureRect(texture, quad, occluded ? occludedColor : color);
            worldHandle.UseShader(null);
            return;
        }

        // The engine's own unshaded sprite layers are drawn the same way.
        worldHandle.UseShader(occluded ? _occlusionShaders!.Draw : null);
        worldHandle.DrawTextureRect(texture, quad, occludedColor);
        worldHandle.UseShader(null);
    }

    /// <summary>
    ///     A colour the default shader wrapper draws unshaded, as the engine draws its own unshaded sprite layers.
    /// </summary>
    private static Color GetUnshadedColor(Color color)
    {
        return new Color(new Vector4(-1f) - color.RGBA);
    }

    /// <summary>
    ///     Draws a closing closet's contents, clipped to its doorway: a port of tgstation's
    ///         <c>/obj/effect/appearance_clone/closet_item</c>, masked by the shut door's alpha. Without it, everything
    ///         inside would vanish the moment the closet started to close, while its doorway was still open.
    /// </summary>
    /// <remarks>
    ///     The contents' layers are drawn here one by one, all through the stencil shader, rather than by
    ///         <c>SpriteSystem.RenderSprite</c>: that switches to each layer's own shader and back to none, so every
    ///         layer after the first that has one would draw unclipped. Layer shaders other than unshaded (such as
    ///         displacement) are dropped for the swing's length instead.
    /// </remarks>
    protected void DrawContents(
        in DrawContext context,
        in DrawEntry entry,
        ShaderInstance resetShader,
        ShaderInstance maskShader,
        ShaderInstance clearShader,
        ShaderInstance drawShader)
    {
        if (!_entityStorageQuery.TryComp(entry.Door.Owner, out var entityStorageComponent) ||
            entityStorageComponent.Contents.ContainedEntities.Count == 0 ||
            !TryGetTexture(entry, entry.StorageVisuals.StateDoorClosed, out var maskTexture))
            return;

        var worldHandle = context.WorldHandle;
        var maskMatrix = entry.BaseLayer.LocalMatrix * entry.SpriteMatrix * context.SnappedWorldMatrix;
        var maskQuad = GetQuad(maskTexture);

        // The mask's bit is cleared across the whole view first: whatever drew before may have left it set anywhere.
        //     The engine sets every stencil bit across the viewport each time it comes back from drawing into another
        //     render target, which the ambient occlusion pass does before entities are drawn, and a set bit lets the
        //     contents through.
        worldHandle.UseShader(resetShader);
        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.DrawRect(context.WorldBounds, Color.White);

        worldHandle.UseShader(maskShader);
        worldHandle.SetTransform(maskMatrix);
        worldHandle.DrawTextureRect(maskTexture, maskQuad);

        // The doorway the contents are clipped to, unshaded (a negative colour) so it shows in the dark too.
        if (DebugMask)
        {
            worldHandle.UseShader(null);
            worldHandle.DrawTextureRect(maskTexture, maskQuad, GetUnshadedColor(DebugMaskColor));
        }

        var contentsTint = DebugMask ? DebugContentsColor : Color.White;

        // Where the closet stands, in the snapped frame its own sprite is drawn in.
        var closetPosition = Vector2.Transform(entry.Position, context.SnappedWorldMatrix);

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
    ///     Masks out of a door the closets that the sprite renderer would draw over it, if the door were part of its
    ///         closet's sprite as it is in tgstation: the ones lower on screen, mostly the closet below in a column.
    ///         They go into the third stencil bit, for <see cref="DrawDoor"/> to draw outside of.
    /// </summary>
    /// <returns>Whether any closet covers the door, so it is to be drawn masked and the mask cleared after.</returns>
    /// <remarks>
    ///     Only closets near the door are looked up, and nothing is drawn unless one is in front of it, so a door
    ///         standing on its own costs one small lookup.
    /// </remarks>
    protected bool MaskOccluders(in DrawContext context, in DrawEntry entry, string? stateName)
    {
        _occluders.Clear();
        if (_occlusionShaders == null || !TryGetTexture(entry, stateName, out var doorTexture))
            return false;

        var doorMatrix = entry.BaseLayer.LocalMatrix * KsClosetDoorSystem.GetDoorTransform(entry.Door.Comp, entry.Angle) * entry.SpriteMatrix;
        var doorQuad = GetQuad(doorTexture);

        _occluderCandidates.Clear();
        _lookupSystem.GetLocalEntitiesIntersecting(context.TreeOwner,
            doorMatrix.TransformBox(doorQuad).Enlarged(OccluderReach),
            _occluderCandidates,
            LookupFlags.Approximate | LookupFlags.Static | LookupFlags.Dynamic);

        var transformQuery = _entityManager.TransformQuery;
        var worldHandle = context.WorldHandle;
        foreach (var candidate in _occluderCandidates)
        {
            if (candidate.Owner == entry.Door.Owner ||
                !_spriteQuery.TryComp(candidate.Owner, out var spriteComponent) ||
                !spriteComponent.Visible ||
                spriteComponent.ContainerOccluded)
                continue;

            // Drawn after this closet, in the sprite renderer's order: deeper, or as deep and lower on screen.
            var transformComponent = transformQuery.GetComponent(candidate.Owner);
            var position = GetTreePosition(transformComponent, context.TreeOwner, context.InvWorldMatrix);
            var screenHeight = GetScreenHeight(position, context.LocalEyeRotation);
            var inFront = spriteComponent.DrawDepth != entry.Sprite.DrawDepth
                ? spriteComponent.DrawDepth > entry.Sprite.DrawDepth
                : screenHeight != entry.ScreenHeight
                    ? screenHeight < entry.ScreenHeight
                    : candidate.Owner.Id > entry.Door.Owner.Id;
            if (!inFront)
                continue;

            // The bit is cleared over the door first, as nothing can be assumed about the stencil here.
            if (_occluders.Count == 0)
            {
                worldHandle.UseShader(_occlusionShaders.Reset);
                worldHandle.SetTransform(doorMatrix * context.SnappedWorldMatrix);
                worldHandle.DrawTextureRect(doorTexture, doorQuad);
                worldHandle.UseShader(_occlusionShaders.Mask);
            }

            var occluder = new Occluder(spriteComponent,
                _transformSystem.GetWorldRotation(transformComponent),
                Vector2.Transform(position, context.SnappedWorldMatrix));
            _occluders.Add(occluder);
            DrawSpriteLayers(worldHandle, occluder.Sprite, context.EyeRotation, occluder.WorldRotation, occluder.WorldPosition, Color.White);
        }

        worldHandle.UseShader(null);
        return _occluders.Count > 0;
    }

    /// <summary>
    ///     Takes back out of the stencil what <see cref="MaskOccluders"/> wrote, so the bit is clear for whatever tests
    ///         the whole value later.
    /// </summary>
    protected void ClearOccluders(in DrawContext context)
    {
        if (_occlusionShaders == null || _occluders.Count == 0)
            return;

        var worldHandle = context.WorldHandle;
        worldHandle.UseShader(_occlusionShaders.Clear);
        foreach (var occluder in _occluders)
            DrawSpriteLayers(worldHandle, occluder.Sprite, context.EyeRotation, occluder.WorldRotation, occluder.WorldPosition, Color.White);

        worldHandle.UseShader(null);
        _occluders.Clear();
    }

    /// <summary>
    ///     Draws a sprite's layers where the engine's sprite renderer puts them, with whatever shader is in use.
    ///         Mirrors <c>SpriteSystem.Render</c> and <c>RenderLayer</c>, without layer shaders or post-shaders.
    ///         Unshaded layers still draw unshaded, through a negative colour, so the shader in use needs the default
    ///         wrapper.
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

            var color = spriteComponent.Color * layer.Color * tint;
            worldHandle.SetTransform(layerMatrix * spriteMatrix);
            worldHandle.DrawTextureRect(texture, GetQuad(texture), layer.ShaderPrototype == SpriteSystem.UnshadedId ? GetUnshadedColor(color) : color);
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
        _snapOccludedShaders?.Dispose();

        base.DisposeBehavior();
    }

    /// <param name="Scale">World units to viewport pixels, y down, as the engine's pixel snap takes it.</param>
    private readonly record struct ViewData(Vector2 Position, Angle Rotation, Vector2 Scale, Vector2 Size);

    /// <param name="TreeOwner">The grid, or map, whose closets are being drawn.</param>
    /// <param name="InvWorldMatrix">The grid's unsnapped inverse world matrix.</param>
    /// <param name="LocalEyeRotation">The eye's rotation, plus the grid's.</param>
    protected readonly record struct DrawContext(
        DrawingHandleWorld WorldHandle,
        EntityUid TreeOwner,
        Matrix3x2 SnappedWorldMatrix,
        Matrix3x2 InvWorldMatrix,
        Angle EyeRotation,
        Angle LocalEyeRotation,
        Box2Rotated WorldBounds);

    /// <summary>
    ///     A closet the sprite renderer draws in front of a door, masked out of it.
    /// </summary>
    private readonly record struct Occluder(SpriteComponent Sprite, Angle WorldRotation, Vector2 WorldPosition);

    /// <param name="Position">Where the closet stands, in its grid's (or map's) frame.</param>
    /// <param name="SpriteMatrix">Places the closet's sprite, after its layers' own matrices, in the grid's frame.</param>
    protected readonly record struct DrawEntry(
        Entity<KsClosetDoorVisualsComponent> Door,
        SpriteComponent Sprite,
        EntityStorageVisualsComponent StorageVisuals,
        SpriteComponent.Layer BaseLayer,
        Vector2 Position,
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
    protected override bool DrawsAt(float angle, bool animating, bool closing)
    {
        // At 0 while swinging, too: the sprite's own door is hidden until the swing is over, and a swing can start
        //     there, with nothing else to draw the door.
        return (angle > 0f || animating) && angle < KsClosetDoorSystem.BackFacingAngle;
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
    ShaderInstance resetShader,
    ShaderInstance maskShader,
    ShaderInstance clearShader,
    ShaderInstance drawShader,
    KsClosetDoorOcclusionShaders occlusionShaders)
    : KsClosetDoorOverlay((int)DrawDepth.SmallObjects, snapShader, snapUnshadedShader, occlusionShaders)
{
    protected override bool DrawsAt(float angle, bool animating, bool closing)
    {
        return closing || angle >= KsClosetDoorSystem.BackFacingAngle;
    }

    protected override void DrawCloset(in DrawContext context, in DrawEntry entry)
    {
        if (entry.Closing)
            DrawContents(context, entry, resetShader, maskShader, clearShader, drawShader);

        if (entry.Angle < KsClosetDoorSystem.BackFacingAngle)
            return;

        // The door's back is drawn over every closet, but in tgstation it is part of its own closet's sprite, under
        //     the closets in front of it. Its front is over mobs in both, so needs none of this.
        var stateName = entry.StorageVisuals.StateBaseClosed + KsClosetDoorVisualsComponent.BackStateSuffix;
        var occluded = OcclusionMask && MaskOccluders(context, entry, stateName);
        DrawDoor(context, entry, stateName, KsClosetDoorFace.Back, occluded);

        if (occluded)
            ClearOccluders(context);
    }
}

/// <summary>
///     The shaders <see cref="KsClosetDoorBackOverlay"/> hides a door behind other closets with, all in the third
///         stencil bit.
/// </summary>
/// <param name="Reset">Clears the bit wherever it is drawn.</param>
/// <param name="Mask">Sets the bit under an occluder's opaque pixels.</param>
/// <param name="Clear">Clears the bit under an occluder's opaque pixels.</param>
/// <param name="Draw">Draws only where the bit is clear.</param>
/// <param name="SnapDraw">The pixel snapping shader, drawn only where the bit is clear. An instance per draw.</param>
public sealed record KsClosetDoorOcclusionShaders(
    ShaderInstance Reset,
    ShaderInstance Mask,
    ShaderInstance Clear,
    ShaderInstance Draw,
    ShaderPrototype SnapDraw);
