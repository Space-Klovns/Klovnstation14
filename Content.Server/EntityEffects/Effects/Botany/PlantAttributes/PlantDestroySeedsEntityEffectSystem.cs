using Content.Shared._KS14.PopupLocalization; // KS14: deferred popup localization
using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Server.Popups;
using Content.Shared.EntityEffects;
using Content.Shared.EntityEffects.Effects.Botany.PlantAttributes;
using Content.Shared.Popups;

namespace Content.Server.EntityEffects.Effects.Botany.PlantAttributes;

public sealed partial class PlantDestroySeedsEntityEffectSystem : EntityEffectSystem<PlantHolderComponent, PlantDestroySeeds>
{
    [Dependency] private PopupSystem _popup = default!;

    protected override void Effect(Entity<PlantHolderComponent> entity, ref EntityEffectEvent<PlantDestroySeeds> args)
    {
        if (entity.Comp.Seed == null || entity.Comp.Dead || entity.Comp.Seed.Immutable)
            return;

        if (entity.Comp.Seed.Seedless)
            return;

        _popup.PopupEntity(
            KsPopupMessage.Create /* KS14: localize popups on the recipient */("botany-plant-seedsdestroyed"),
            entity,
            PopupType.SmallCaution
        );
        entity.Comp.Seed.Seedless = true;
    }
}
