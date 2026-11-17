using Content.Shared._KS14.Fluidmos.Ice;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.Fluidmos.Ice;

/// <summary>
///     Tints an ice sheet by what froze into it, so a thin rime and a thick sheet read differently and frozen blood is red.
/// </summary>
public sealed partial class KsIceSheetVisualizerSystem : VisualizerSystem<KsIceSheetComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, KsIceSheetComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } spriteComponent ||
            !AppearanceSystem.TryGetData<Color>(uid, KsIceSheetVisuals.Color, out var color, args.Component))
            return;

        SpriteSystem.SetColor((uid, spriteComponent), color);
    }
}
