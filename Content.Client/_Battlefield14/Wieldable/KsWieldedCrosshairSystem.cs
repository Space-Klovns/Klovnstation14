using Content.Client.CombatMode;
using Content.Client.Hands.Systems;
using Content.Shared._Battlefield14.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._Battlefield14.Wieldable;

/// <summary>
///     Finds the crosshair of the gun held wielded in the active hand (<see cref="KsWieldedCrosshairComponent"/>), which
///         <see cref="CombatModeIndicatorsOverlay"/> draws, and hides the system cursor while there is one.
/// </summary>
public sealed partial class KsWieldedCrosshairSystem : EntitySystem
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private CombatModeSystem _combatModeSystem = default!;
    [Dependency] private HandsSystem _handsSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private EntityQuery<KsWieldedCrosshairComponent> _crosshairQuery = default!;
    [Dependency] private EntityQuery<WieldableComponent> _wieldableQuery = default!;

    /// <summary>
    ///     A blank cursor, made the first time the system cursor is hidden.
    /// </summary>
    private ICursor? _blankCursor;

    /// <summary>
    ///     Whether this system hid the system cursor, so it puts back only what it took away.
    /// </summary>
    private bool _cursorHidden;

    public override void Shutdown()
    {
        base.Shutdown();

        if (_cursorHidden)
            _clyde.SetCursor(null);

        _cursorHidden = false;
        _blankCursor?.Dispose();
        _blankCursor = null;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var showCrosshair = GetActiveCrosshair() != null;
        if (showCrosshair == _cursorHidden)
            return;

        if (showCrosshair)
        {
            _blankCursor ??= _clyde.CreateCursor(new Image<Rgba32>(1, 1), Vector2i.Zero);
            _clyde.SetCursor(_blankCursor);
        }
        else
        {
            _clyde.SetCursor(null);
        }

        _cursorHidden = showCrosshair;
    }

    /// <summary>
    ///     The crosshair to draw at the cursor, or null for the ordinary combat-mode sight: only in combat mode, and only
    ///         for a gun with its own crosshair held wielded in the active hand. Also null while the player has the
    ///         combat-mode indicators turned off, as nothing would draw it and the cursor must not be hidden.
    /// </summary>
    public Texture? GetActiveCrosshair()
    {
        if (!_combatModeSystem.IsInCombatMode() ||
            !_overlayManager.HasOverlay<CombatModeIndicatorsOverlay>() ||
            _handsSystem.GetActiveHandEntity() is not { } heldUid ||
            !_crosshairQuery.TryComp(heldUid, out var crosshairComponent) ||
            !_wieldableQuery.TryComp(heldUid, out var wieldableComponent) ||
            !wieldableComponent.Wielded)
        {
            return null;
        }

        return _spriteSystem.Frame0(crosshairComponent.Crosshair);
    }
}
