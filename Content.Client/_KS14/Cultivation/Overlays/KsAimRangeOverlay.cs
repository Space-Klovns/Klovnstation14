using System.Numerics;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Actions.Components;
using Content.Shared.CCVar;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._KS14.Cultivation.Overlays;

/// <summary>
///     While aiming a cultivation art, the art's icon at the cursor turns red when the spot under the mouse is out of its
///         reach, so there's no guessing why a click did nothing.
/// </summary>
public sealed partial class KsAimRangeOverlay : Overlay
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

    private readonly SpriteSystem _spriteSystem;
    private readonly SharedTransformSystem _transformSystem;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private static readonly Color OutOfRange = new(1f, 0.25f, 0.2f, 0.95f);

    public KsAimRangeOverlay()
    {
        IoCManager.InjectDependencies(this);
        _spriteSystem = _entityManager.System<SpriteSystem>();
        _transformSystem = _entityManager.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_uiManager.GetUIController<ActionUIController>().SelectingTargetFor is not { } actionUid
            || !_entityManager.HasComponent<CultivationArtCategoryComponent>(actionUid)
            || !_entityManager.TryGetComponent<ActionComponent>(actionUid, out var action)
            || !_entityManager.TryGetComponent<TargetActionComponent>(actionUid, out var target)
            || target.Range <= 0f || action.Icon == null
            || _playerManager.LocalEntity is not { } player)
            return;

        var mousePos = _inputManager.MouseScreenPosition;
        if (mousePos.Window == WindowId.Invalid)
            return;

        var mouseMap = _eyeManager.PixelToMap(mousePos);
        var playerMap = _transformSystem.GetMapCoordinates(player);
        if (mouseMap.MapId != playerMap.MapId || (mouseMap.Position - playerMap.Position).Length() <= target.Range)
            return;

        var texture = _spriteSystem.Frame0(action.Icon);
        var offset = _cfg.GetCVar(CCVars.HudHeldItemOffset);
        args.ScreenHandle.DrawTexture(texture, mousePos.Position - texture.Size / 2 + new Vector2(offset, offset), OutOfRange);
    }
}
