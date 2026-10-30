using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Popups;
using Robust.Shared.Spawners;

namespace Content.Server._KS14.Spawner;

public sealed partial class KsPopupOnDespawnSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    [SubscribeLocalEvent]
    private void OnDespawn(Entity<KsPopupOnDespawnComponent> entity, ref TimedDespawnEvent args)
    {
        // At the coordinates rather than on the entity, which is deleted straight after this.
        _popupSystem.PopupCoordinates(KsPopupMessage.Create(entity.Comp.Message), Transform(entity).Coordinates, type: entity.Comp.PopupType);
    }
}
