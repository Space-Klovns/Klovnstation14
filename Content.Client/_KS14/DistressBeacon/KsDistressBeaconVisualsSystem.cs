using Content.Client._KS14.Graphics;
using Content.Shared._KS14.DistressBeacon;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.DistressBeacon;

/// <summary>
///     The look of a beacon: its lit icon while its wearer is down, its light, which pulses fast and bright for a
///         critical wearer and slow and dim for a dead one, and an outline on the wearer that pulses with it.
/// </summary>
public sealed partial class KsDistressBeaconVisualsSystem : EntitySystem
{
    private const string IconState = "icon";
    private const string LitIconState = "on-icon";

    private static readonly ProtoId<ShaderPrototype> OutlineShader = "KsDistressBeaconOutline";

    /// <summary>
    ///     How thick the outline is, in screen pixels.
    /// </summary>
    private const float OutlineWidth = 2f;

    /// <summary>
    ///     The share of its peak the light never drops below, so it never quite goes out.
    /// </summary>
    private const float LightFloor = 0.15f;

    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private SharedPointLightSystem _pointLightSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<KsDistressBeaconVisualsComponent> _visualsQuery = default!;

    /// <summary>
    ///     The pulse at a moment, from 0 at the trough to 1 at the peak. The one source of it: the light and the
    ///         outline are both worked out from this, so they cannot drift apart.
    /// </summary>
    public static float PulseWave(float seconds, float pulseRate)
    {
        return 0.5f - 0.5f * MathF.Cos(seconds * pulseRate * MathF.Tau);
    }

    /// <summary>
    ///     The light's energy for a pulse: a faint glow at the trough, the settings' peak at the peak.
    /// </summary>
    public static float LightEnergyFor(KsDistressBeaconStateSettings settings, float wave)
    {
        return settings.LightEnergy * (LightFloor + (1f - LightFloor) * wave);
    }

    /// <summary>
    ///     The outline's opacity for a pulse: the settings' minimum at the trough, fully opaque at the peak.
    /// </summary>
    public static float OutlineAlphaFor(KsDistressBeaconStateSettings settings, float wave)
    {
        return settings.OutlineMinAlpha + (1f - settings.OutlineMinAlpha) * wave;
    }

    [SubscribeLocalEvent]
    private void OnAfterAutoHandleState(Entity<KsDistressBeaconComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        ApplyState(entity);
    }

    // The client changes the state itself when it predicts its wearer going down, which this does not follow from the
    //     server's state.
    [SubscribeLocalEvent]
    private void OnStateChanged(Entity<KsDistressBeaconComponent> entity, ref KsDistressBeaconStateChangedEvent args)
    {
        ApplyState(entity);
    }

    /// <summary>
    ///     Brings what is drawn in step with the state: the icon, the light's colour for it, and the wearer's outline.
    /// </summary>
    private void ApplyState(Entity<KsDistressBeaconComponent> entity)
    {
        // The light's colour for the state, so it follows the state even where the light's own state lags.
        var settings = entity.Comp.SettingsFor(entity.Comp.State);
        if (settings != null)
            _pointLightSystem.SetColor(entity.Owner, settings.LightColor);

        if (_spriteQuery.TryComp(entity, out var spriteComponent))
        {
            var state = settings == null ? IconState : LitIconState;
            _spriteSystem.LayerSetRsiState((entity.Owner, spriteComponent), 0, state);
        }

        RefreshOutline(entity);
    }

    [SubscribeLocalEvent]
    private void OnInserted(Entity<KsDistressBeaconComponent> entity, ref EntGotInsertedIntoContainerMessage args)
    {
        RefreshOutline(entity);
    }

    [SubscribeLocalEvent]
    private void OnRemoved(Entity<KsDistressBeaconComponent> entity, ref EntGotRemovedFromContainerMessage args)
    {
        RefreshOutline(entity);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsDistressBeaconComponent> entity, ref ComponentShutdown args)
    {
        if (!_visualsQuery.TryComp(entity, out var visualsComponent))
            return;

        RemoveOutline((entity.Owner, visualsComponent));
        RemCompDeferred<KsDistressBeaconVisualsComponent>(entity.Owner);
    }

    /// <summary>
    ///     Puts the outline on whoever wears the beacon while it is on, and takes it off whoever it was on when it
    ///         isn't, or they aren't wearing it any more.
    /// </summary>
    private void RefreshOutline(Entity<KsDistressBeaconComponent> entity)
    {
        if (TerminatingOrDeleted(entity))
            return;

        EntityUid? wearerUid = null;
        if (entity.Comp.SettingsFor(entity.Comp.State) != null &&
            _containerSystem.TryGetContainingContainer(entity.Owner, out var container))
            wearerUid = container.Owner;

        var visualsComponent = EnsureComp<KsDistressBeaconVisualsComponent>(entity);

        if (visualsComponent.OutlinedUid == wearerUid)
            return;

        RemoveOutline((entity.Owner, visualsComponent));

        if (wearerUid is not { } wearer || !_spriteQuery.TryComp(wearer, out var wearerSprite))
            return;

        var shader = ProtoMan.Index(OutlineShader).InstanceUnique();
        // Every uniform, so that it can never draw with another instance's.
        shader.SetParameter("outlineColor", Color.White);
        shader.SetParameter("outlineWidth", OutlineWidth);
        visualsComponent.Shader = shader;
        visualsComponent.OutlinedUid = wearer;

        // After whatever else the sprite has, such as a dissolve or the down flash.
        _spriteSystem.SetPostShader((wearer, wearerSprite), new SpriteComponent.PostShaderArgs(KsPostShaderIds.DistressBeaconOutline, shader)
        {
            After = KsPostShaderIds.AfterBaseEffects,
        });
    }

    private void RemoveOutline(Entity<KsDistressBeaconVisualsComponent> entity)
    {
        if (entity.Comp.OutlinedUid is { } outlinedUid &&
            !TerminatingOrDeleted(outlinedUid) &&
            _spriteQuery.TryComp(outlinedUid, out var outlinedSprite))
            _spriteSystem.RemovePostShader((outlinedUid, outlinedSprite), KsPostShaderIds.DistressBeaconOutline);

        entity.Comp.OutlinedUid = null;
        entity.Comp.Shader?.Dispose();
        entity.Comp.Shader = null;
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

            // One pulse for both the light and the outline.
            var wave = PulseWave(seconds, settings.PulseRate);

            // Only how the light looks this frame: the server owns it, and never hears of this.
            _pointLightSystem.SetEnergy(uid, LightEnergyFor(settings, wave), lightComponent);

            if (!_visualsQuery.TryComp(uid, out var visualsComponent))
                continue;

            visualsComponent.Wave = wave;
            visualsComponent.OutlineAlpha = OutlineAlphaFor(settings, wave);
            visualsComponent.Shader?.SetParameter("outlineColor", settings.LightColor.WithAlpha(visualsComponent.OutlineAlpha));
        }
    }
}
