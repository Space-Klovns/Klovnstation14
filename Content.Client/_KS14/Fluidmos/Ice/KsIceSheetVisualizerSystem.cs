using Content.Client.IconSmoothing;
using Content.Shared._KS14.Fluidmos.Ice;
using Content.Shared.Fluids;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.Fluidmos.Ice;

/// <summary>
///     Draws an ice sheet as the puddle it froze out of, frozen: the same shape by how full it is, small splats for a thin
///         rime and the tile-filling, smoothed sheet once it would be a full puddle, tinted by what froze into it so frozen
///         blood is red.
/// </summary>
/// <remarks>
///     Picks its state as the client's puddle appearance does, against the same thresholds, from a sprite drawn on the
///         puddle's shapes. Only runs when the server changes the sheet's appearance, so a sheet that sits there costs
///         nothing.
/// </remarks>
public sealed partial class KsIceSheetVisualizerSystem : VisualizerSystem<KsIceSheetComponent>
{
    [Dependency] private IconSmoothSystem _iconSmoothSystem = default!;
    [Dependency] private EntityQuery<IconSmoothComponent> _iconSmoothQuery = default!;

    protected override void OnAppearanceChange(EntityUid uid, KsIceSheetComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } spriteComponent)
            return;

        if (AppearanceSystem.TryGetData<Color>(uid, KsIceSheetVisuals.Color, out var color, args.Component))
            SpriteSystem.SetColor((uid, spriteComponent), color);

        if (!AppearanceSystem.TryGetData<float>(uid, KsIceSheetVisuals.Fill, out var fill, args.Component) ||
            !_iconSmoothQuery.TryComp(uid, out var iconSmoothComponent))
            return;

        if (fill < SharedPuddleSystem.LowThreshold)
            SetUnsmoothedState((uid, spriteComponent), iconSmoothComponent, "a");
        else if (fill < SharedPuddleSystem.MediumThreshold)
            SetUnsmoothedState((uid, spriteComponent), iconSmoothComponent, "b");
        else if (!iconSmoothComponent.Enabled)
        {
            // Smoothing sets the state from its neighbours, and joins it onto any sheet beside it.
            SpriteSystem.LayerSetRsiState((uid, spriteComponent), 0, $"{iconSmoothComponent.StateBase}0");
            _iconSmoothSystem.SetEnabled(uid, true, iconSmoothComponent);
        }
    }

    private void SetUnsmoothedState(Entity<SpriteComponent> sprite, IconSmoothComponent iconSmoothComponent, string suffix)
    {
        SpriteSystem.LayerSetRsiState(sprite.AsNullable(), 0, $"{iconSmoothComponent.StateBase}{suffix}");
        _iconSmoothSystem.SetEnabled(sprite.Owner, false, iconSmoothComponent);
    }
}
