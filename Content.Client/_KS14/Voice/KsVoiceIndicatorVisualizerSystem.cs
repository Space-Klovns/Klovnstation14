using Content.Shared._KS14.Voice;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.Voice;

/// <summary>
///     Draws the talking icon above players who are transmitting voice, following the typing indicator's approach.
/// </summary>
public sealed partial class KsVoiceIndicatorVisualizerSystem : VisualizerSystem<KsVoiceIndicatorComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, KsVoiceIndicatorComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        var spriteEntity = (uid, args.Sprite);
        if (!SpriteSystem.LayerMapTryGet(spriteEntity, KsVoiceIndicatorLayers.Base, out var layer, logMissing: false))
        {
            layer = SpriteSystem.LayerMapReserve(spriteEntity, KsVoiceIndicatorLayers.Base);
            SpriteSystem.LayerSetRsi(spriteEntity, layer, component.Sprite.RsiPath, component.Sprite.RsiState);
            if (component.Shader != null)
                args.Sprite.LayerSetShader(layer, component.Shader);

            SpriteSystem.LayerSetOffset(spriteEntity, layer, component.Offset);
        }

        AppearanceSystem.TryGetData<bool>(uid, KsVoiceVisuals.Talking, out var talking, component: args.Component);
        SpriteSystem.LayerSetVisible(spriteEntity, layer, talking);
    }
}
