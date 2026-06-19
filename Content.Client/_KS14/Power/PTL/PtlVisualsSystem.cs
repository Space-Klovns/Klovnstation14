using Content.Shared._KS14.Power.PTL;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.Power.PTL;

public sealed partial class PtlVisualsSystem : VisualizerSystem<PtlVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, PtlVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } spriteComponent)
            return;

        AppearanceSystem.TryGetData<bool>(uid, PtlVisuals.Active, out var active, args.Component);
        SpriteSystem.LayerSetVisible((uid, spriteComponent), PtlVisualLayers.Unpowered, active);

        if (AppearanceSystem.TryGetData<int>(uid, PtlVisuals.ChargeLevel, out var chargeLevel, args.Component))
        {
            var chargeVisible = active && chargeLevel > 0;
            SpriteSystem.LayerSetVisible((uid, spriteComponent), PtlVisualLayers.Charge, chargeVisible);

            if (chargeVisible)
                SpriteSystem.LayerSetRsiState((uid, spriteComponent), PtlVisualLayers.Charge, $"{component.ChargePrefix}{chargeLevel}");
        }
    }
}
