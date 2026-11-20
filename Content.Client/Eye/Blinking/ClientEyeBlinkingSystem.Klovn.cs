using Content.Shared._KS14.CCVar;
using Content.Shared.Body;
using Content.Shared.Eye.Blinking;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;

namespace Content.Client.Eye.Blinking;

/// <summary>
///     Eyelid layers live on the holder's sprite, not the eyes organ's, so they have to follow the eyes in and out of
///         bodies, and hide with whatever layers cover them.
/// </summary>
public sealed partial class ClientEyeBlinkingSystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private EntityQuery<HideableHumanoidLayersComponent> _hideableLayersQuery = default!;

    private bool _periodicBlinking = true;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.BlinkingPeriodic, value => _periodicBlinking = value, invokeImmediately: true);
    }

    protected override void OnEyesAttached(Entity<EyeBlinkingComponent> ent, EntityUid holderUid)
    {
        if (ent.Comp.Body is { } previousHolderUid && previousHolderUid != holderUid)
            KsRemoveEyelidLayers(ent, previousHolderUid);

        InitEyeBlinking(ent, holderUid);
    }

    protected override void OnEyesDetached(Entity<EyeBlinkingComponent> ent, EntityUid holderUid)
    {
        KsRemoveEyelidLayers(ent, holderUid);
    }

    [SubscribeLocalEvent]
    private void OnHolderLayerVisibilityChanged(Entity<EyeBlinkingComponent> ent, ref BodyRelayedEvent<HumanoidLayerVisibilityChangedEvent> args)
    {
        if (!ent.Comp.KsHidingLayers.Contains(args.Args.Layer))
            return;

        if (ent.Comp.Body is not { } holderUid || !_spriteQuery.TryComp(holderUid, out var spriteComponent))
            return;

        var occluded = KsIsOccluded(holderUid, ent.Comp);
        foreach (var eyelidState in ent.Comp.Eyelids)
        {
            if (_sprite.LayerMapTryGet((holderUid, spriteComponent), eyelidState.LayerKey, out var layerIndex, logMissing: false))
                _sprite.LayerSetVisible((holderUid, spriteComponent), layerIndex, eyelidState.IsClosed && !occluded);
        }
    }

    /// <summary>
    ///     Whether any of the holder's layers that cover these eyelids is hidden.
    /// </summary>
    private bool KsIsOccluded(EntityUid holderUid, EyeBlinkingComponent blinkingComponent)
    {
        if (!_hideableLayersQuery.TryComp(holderUid, out var hideableLayersComponent)
            || hideableLayersComponent.HiddenLayers.Count == 0)
            return false;

        foreach (var hiddenLayer in hideableLayersComponent.HiddenLayers.Keys)
        {
            if (blinkingComponent.KsHidingLayers.Contains(hiddenLayer))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Pushes the next periodic blink a fresh interval away, so that it neither fires now nor the moment periodic
    ///         blinking is turned back on.
    /// </summary>
    private void KsDeferBlink(EyeBlinkingComponent blinkingComponent, TimeSpan curTime)
    {
        blinkingComponent.NextBlinkingTime = curTime + _random.Next(blinkingComponent.MinBlinkInterval, blinkingComponent.MaxBlinkInterval);
    }

    /// <summary>
    ///     Takes these eyes' eyelid layers off a holder's sprite and forgets the holder.
    /// </summary>
    private void KsRemoveEyelidLayers(Entity<EyeBlinkingComponent> ent, EntityUid holderUid)
    {
        if (_spriteQuery.TryComp(holderUid, out var spriteComponent))
            KsRemoveEyelidSpriteLayers(ent, (holderUid, spriteComponent));

        ent.Comp.Eyelids.Clear();
        ent.Comp.BlinkInProgress = false;

        if (ent.Comp.Body == holderUid)
            ent.Comp.Body = null;
    }

    /// <summary>
    ///     Removes these eyes' eyelid and eyelid displacement layers from a holder's sprite.
    /// </summary>
    private void KsRemoveEyelidSpriteLayers(Entity<EyeBlinkingComponent> ent, Entity<SpriteComponent> holder)
    {
        foreach (var eyelidState in ent.Comp.Eyelids)
        {
            _sprite.RemoveLayer(holder.AsNullable(), eyelidState.LayerKey, logMissing: false);
            if (eyelidState.DisplacementKey != null)
                _sprite.RemoveLayer(holder.AsNullable(), eyelidState.DisplacementKey, logMissing: false);
        }
    }
}
