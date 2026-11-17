using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;

namespace Content.Shared._KS14.ShoveWeapon;

public sealed partial class KsShoveWeaponSystem : EntitySystem
{
    [Dependency] private ItemToggleSystem _itemToggleSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private MovementModStatusSystem _movementModStatusSystem = default!;

    [SubscribeLocalEvent]
    private void OnDisarmAttempt(Entity<KsShoveWeaponComponent> entity, ref KsDisarmWithWeaponAttemptEvent args)
    {
        if (!_itemToggleSystem.IsActivated(entity.Owner))
        {
            args.Cancelled = true;
            _popupSystem.PopupEntity(KsPopupMessage.Create(entity.Comp.InactivePopup, ("item", entity.Owner)), args.User, args.User);
            return;
        }

        if (entity.Comp.Guaranteed)
            args.Chance = 0f;

        if (entity.Comp.SuccessSound is { } successSound)
            args.SuccessSound = successSound;
    }

    /// <summary>
    ///     Refreshes the stagger rather than adding to it, so shoving someone over and over can't slow them for good.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnShoved(Entity<KsShoveWeaponComponent> entity, ref KsShovedWithWeaponEvent args)
    {
        _movementModStatusSystem.TryUpdateMovementSpeedModDuration(args.Target,
            entity.Comp.StaggerEffect,
            entity.Comp.StaggerDuration,
            entity.Comp.StaggerSpeedModifier);
    }
}
