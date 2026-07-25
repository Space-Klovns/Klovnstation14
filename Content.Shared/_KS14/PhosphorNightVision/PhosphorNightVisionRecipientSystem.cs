using Content.Shared._Mono.Overlays;
using Content.Shared.Flash;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.PhosphorNightVision;

public sealed partial class PhosphorNightVisionRecipientSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PhosphorNightVisionRecipientComponent, AfterFlashedEvent>(OnAfterFlashed);
    }

    private void OnAfterFlashed(Entity<PhosphorNightVisionRecipientComponent> entity, ref AfterFlashedEvent args)
    {
        if (entity.Owner != args.Target)
            return;

        entity.Comp.LastFlashTime = _gameTiming.CurTime;
        entity.Comp.LastFlashDuration = args.FlashDuration;

        if (!_gameTiming.IsFirstTimePredicted ||
            entity.Comp.NightVisionSourceUid is not { } sourceUid ||
            !TryComp<PhosphorNightVisionComponent>(sourceUid, out var nightVisionComponent) ||
            !nightVisionComponent.Enabled)
            return;

        // anything but relaying events
        _popupSystem.PopupClient(Loc.GetString("ks-phosphor-nightvision-popup-flash"), entity, entity);
    }
}
