using Content.Shared._KS14.Power.PTL;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.Power.PTL;

public sealed partial class PTLVisualsSystem : VisualizerSystem<PTLVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, PTLVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        AppearanceSystem.TryGetData<bool>(uid, PTLVisuals.Active, out var active, args.Component);
        args.Sprite.LayerSetVisible(PTLVisualLayers.Unpowered, active);

        if (AppearanceSystem.TryGetData<int>(uid, PTLVisuals.ChargeLevel, out var chargeLevel, args.Component))
        {
            var chargeVisible = active && chargeLevel > 0;
            args.Sprite.LayerSetVisible(PTLVisualLayers.Charge, chargeVisible);

            if (chargeVisible)
            {
                args.Sprite.LayerSetState(PTLVisualLayers.Charge, $"{component.ChargePrefix}{chargeLevel}");
            }
        }
    }
}
