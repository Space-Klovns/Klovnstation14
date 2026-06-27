using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.Hands.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.RCD;

public sealed class AlignRCDConstruction : PlacementMode
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    private readonly SharedMapSystem _mapSystem;
    private readonly HandsSystem _handsSystem;
    private readonly RCDSystem _rcdSystem;
    private readonly SharedTransformSystem _transformSystem;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IStateManager _stateManager = default!;

    private const float SearchBoxSize = 2f;
    private const float PlaceColorBaseAlpha = 0.5f;

    private EntityCoordinates _unalignedMouseCoords = default;


    private Color _invalidInRangeColor; // KS14
    private Color _invalidOutOfRangeColor = Color.FromHex("#8e2fad"); // KS14

    /// <summary>
    /// This placement mode is not on the engine because it is content specific (i.e., for the RCD)
    /// </summary>
    public AlignRCDConstruction(PlacementManager pMan) : base(pMan)
    {
        IoCManager.InjectDependencies(this);
        _mapSystem = _entityManager.System<SharedMapSystem>();
        _handsSystem = _entityManager.System<HandsSystem>();
        _rcdSystem = _entityManager.System<RCDSystem>();
        _transformSystem = _entityManager.System<SharedTransformSystem>();
        _spriteSystem = _entityManager.System<Robust.Client.GameObjects.SpriteSystem>(); // KS14

        ValidPlaceColor = ValidPlaceColor.WithAlpha(PlaceColorBaseAlpha);
        // KS14 Start
        InvalidPlaceColor = InvalidPlaceColor.WithAlpha(PlaceColorBaseAlpha); // KS14

        _invalidInRangeColor = InvalidPlaceColor/* intentional */.WithAlpha(PlaceColorBaseAlpha);
        _invalidOutOfRangeColor = _invalidOutOfRangeColor.WithAlpha(PlaceColorBaseAlpha * 0.5f); // KS14
        // KS14 End
    }

    public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
    {
        _unalignedMouseCoords = ScreenToCursorGrid(mouseScreen);
        MouseCoords = _unalignedMouseCoords.AlignWithClosestGridTile(SearchBoxSize, _entityManager, _mapManager);

        var gridId = _transformSystem.GetGrid(MouseCoords);

        if (!_entityManager.TryGetComponent<MapGridComponent>(gridId, out var mapGrid))
            return;

        CurrentTile = _mapSystem.GetTileRef(gridId.Value, mapGrid, MouseCoords);

        float tileSize = mapGrid.TileSize;
        GridDistancing = tileSize;

        if (pManager.CurrentPermission!.IsTile)
        {
            MouseCoords = new EntityCoordinates(MouseCoords.EntityId, new Vector2(CurrentTile.X + tileSize / 2,
                CurrentTile.Y + tileSize / 2));
        }
        else
        {
            MouseCoords = new EntityCoordinates(MouseCoords.EntityId, new Vector2(CurrentTile.X + tileSize / 2 + pManager.PlacementOffset.X,
                CurrentTile.Y + tileSize / 2 + pManager.PlacementOffset.Y));
        }
    }

    public override bool IsValidPosition(EntityCoordinates position)
    {
        var player = _playerManager.LocalSession?.AttachedEntity;

        // If the destination is out of interaction range, set the placer alpha to zero
        if (!_entityManager.TryGetComponent<TransformComponent>(player, out var xform))
            return false;

        if (!_transformSystem.InRange(xform.Coordinates, position, SharedInteractionSystem.InteractionRange))
        {
            InvalidPlaceColor = _invalidOutOfRangeColor; // KS14: Use fixed color instead of setting alpha //InvalidPlaceColor.WithAlpha(0);
            return false;
        }

        // Otherwise restore the alpha value
        else
        {
            InvalidPlaceColor = _invalidInRangeColor; // KS14: Use fixed color instead of setting alpha //InvalidPlaceColor.WithAlpha(0);
        }

        // Determine if player is carrying an RCD in their active hand
        if (!_handsSystem.TryGetActiveItem(player.Value, out var heldEntity))
            return false;

        if (!_entityManager.TryGetComponent<RCDComponent>(heldEntity, out var rcd))
            return false;

        var gridUid = _transformSystem.GetGrid(position);
        if (!_entityManager.TryGetComponent<MapGridComponent>(gridUid, out var mapGrid))
            return false;
        var tile = _mapSystem.GetTileRef(gridUid.Value, mapGrid, position);
        var posVector = _mapSystem.TileIndicesFor(gridUid.Value, mapGrid, position);

        // Determine if the user is hovering over a target
        var currentState = _stateManager.CurrentState;

        if (currentState is not GameplayStateBase screen)
            return false;

        var target = screen.GetClickedEntity(_transformSystem.ToMapCoordinates(_unalignedMouseCoords));

        // Determine if the RCD operation is valid or not
        if (!_rcdSystem.IsRCDOperationStillValid(heldEntity.Value, rcd, gridUid.Value, mapGrid, tile, posVector, target, player.Value, false))
            return false;

        return true;
    }

    // KS14 Start
    [Dependency] private readonly Robust.Shared.Timing.IGameTiming _gameTiming = default!;
    private Robust.Client.GameObjects.SpriteSystem _spriteSystem;

    private static readonly Robust.Shared.Utility.SpriteSpecifier RotArrowSprite = new Robust.Shared.Utility.SpriteSpecifier.Rsi(new("/Textures/Markers/teg_arrow.rsi"), "arrow");

    public override void Render(in Robust.Client.Graphics.OverlayDrawArgs args)
    {
        var uid = pManager.CurrentPlacementOverlayEntity;
        if (!pManager.EntityManager.TryGetComponent(uid, out Robust.Client.GameObjects.SpriteComponent? spriteComponent) ||
            !spriteComponent.Visible ||
            !pManager.EntityManager.TryGetComponent(uid, out TransformComponent? transformComponent))
        {
            return;
        }

        var locationcollection = pManager.PlacementType switch
        {
            PlacementManager.PlacementTypes.None => SingleCoordinate(),
            PlacementManager.PlacementTypes.Line => LineCoordinates(),
            PlacementManager.PlacementTypes.Grid => GridCoordinates(),
            _ => SingleCoordinate()
        };

        var directionAngle = pManager.Direction.ToAngle();
        var worldHandle = args.WorldHandle;

        var arrowTexture = _spriteSystem.GetFrame(RotArrowSprite, _gameTiming.CurTime, loop: true);
        var spriteBounds = _spriteSystem.GetLocalBounds((uid.Value, spriteComponent));

        var topRight = spriteBounds.TopRight;
        var arrowOffset = new Vector2(0f, spriteBounds.Height * -0.75f);
        var eyeRotation = args.Viewport.Eye?.Rotation ?? default;
        var dontDrawArrow = pManager.EntityManager.HasComponent<_KS14.Rcd.KsRcdPlacementNoHintComponent>(uid);

        foreach (var coordinate in locationcollection)
        {
            if (!coordinate.IsValid(pManager.EntityManager))
                continue;

            var worldPos = _transformSystem.ToMapCoordinates(coordinate).Position;
            var worldRot = _transformSystem.GetWorldRotation(coordinate.EntityId) + directionAngle;

            var respectiveColor = IsValidPosition(coordinate) ? ValidPlaceColor : InvalidPlaceColor;

            _spriteSystem.SetColor((uid.Value, spriteComponent), respectiveColor);
            _spriteSystem.RenderSprite((uid.Value, spriteComponent), args.WorldHandle, eyeRotation, worldRot, worldPos);

            if (dontDrawArrow ||
                transformComponent.NoLocalRotation)
                continue;

            // TODO LCDC: Fix chemical beacon somehow being locked to only north/south idk how

            // Just ArrowOffset because the worldhandle matrix is relative to the sprite kinda
            // also -topRight because the above is relative to the top-right of the sprite, not center
            worldHandle.SetTransform(Matrix3Helpers.CreateTransform(worldPos, -eyeRotation) * spriteComponent.LocalMatrix);
            worldHandle.DrawTexture(arrowTexture, directionAngle.RotateVec(arrowOffset) - topRight, directionAngle, modulate: respectiveColor);
        }
    }
    // KS14 End
}
