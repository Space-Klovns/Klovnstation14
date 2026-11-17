using Content.Shared._KS14.PopupLocalization; // KS14: deferred popup localization
using Content.Server.Damage.Components;
using Content.Server.Popups;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server.Damage.Systems;

/// <summary>
/// Outputs a random pop-up from the strings list when an object receives damage
/// </summary>
public sealed partial class DamageRandomPopupSystem : EntitySystem
{
    [Dependency] private PopupSystem _popupSystem = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DamageRandomPopupComponent, DamageChangedEvent>(OnDamageChange);
    }

    private void OnDamageChange(EntityUid uid, DamageRandomPopupComponent component, DamageChangedEvent args)
    {
        _popupSystem.PopupEntity(KsPopupMessage.Create /* KS14: localize popups on the recipient */(_random.Pick(component.Popups)), uid);
    }
}
