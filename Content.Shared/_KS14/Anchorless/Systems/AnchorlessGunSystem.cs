using Content.Shared._KS14.Anchorless.Components;
using Content.Shared.Flash;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Popups;

namespace Content.Shared._KS14.Anchorless.Systems;

public sealed partial class AnchorlessGunSystem : EntitySystem
{
    [Dependency] private SharedFlashSystem _flash = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KsAnchorlessAntagComponent, SelfBeforeGunShotEvent>(BeforeGunShotEvent);
    }
    private void BeforeGunShotEvent(Entity<KsAnchorlessAntagComponent> ent, ref SelfBeforeGunShotEvent args)
    {
        // we don't cancel shooting the gun, we just make it impossible for the anchorless to use guns effectively
        _flash.Flash(ent.Owner, null, null, ent.Comp.GunFlashDuration, ent.Comp.GunFlashSlowdown, false, false, null, true);

        _popup.PopupPredicted(Loc.GetString("anchorless-gun-flash-message"), ent, ent);
    }
}
