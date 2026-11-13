using Content.Shared._KS14.DistressBeacon;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._KS14.DistressBeacon;

/// <summary>
///     The look of a beacon: its lit icon while its wearer is down, and its light, which pulses fast and bright for a
///         critical wearer and slow and dim for a dead one.
/// </summary>
public sealed partial class KsDistressBeaconVisualsSystem : EntitySystem
{
    private const string IconState = "icon";
    private const string LitIconState = "on-icon";

    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private SharedPointLightSystem _pointLightSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    [SubscribeLocalEvent]
    private void OnAfterAutoHandleState(Entity<KsDistressBeaconComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        // The light's colour for the state, so it follows the state even where the light's own state lags.
        var settings = entity.Comp.SettingsFor(entity.Comp.State);
        if (settings != null)
            _pointLightSystem.SetColor(entity.Owner, settings.LightColor);

        if (!_spriteQuery.TryComp(entity, out var spriteComponent))
            return;

        var state = entity.Comp.State == KsDistressBeaconState.Off ? IconState : LitIconState;
        _spriteSystem.LayerSetRsiState((entity.Owner, spriteComponent), 0, state);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var seconds = (float)(_gameTiming.CurTime + _gameTiming.TickRemainder).TotalSeconds;

        var enumerator = EntityQueryEnumerator<KsDistressBeaconComponent, PointLightComponent>();
        while (enumerator.MoveNext(out var uid, out var beaconComponent, out var lightComponent))
        {
            var settings = beaconComponent.SettingsFor(beaconComponent.State);
            if (settings == null)
                continue;

            // A smooth rise and fall between a faint glow and the peak, so the light never quite goes out.
            var wave = 0.5f - 0.5f * MathF.Cos(seconds * settings.PulseRate * MathF.Tau);
            // Only how the light looks this frame: the server owns it, and never hears of this.
            _pointLightSystem.SetEnergy(uid, settings.LightEnergy * (0.15f + 0.85f * wave), lightComponent);
        }
    }
}
