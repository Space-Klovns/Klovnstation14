using Content.Server.Light.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Light.Components;

namespace Content.Server._KS14.Light;

/// <summary>
///     Switches on a <see cref="KsHandheldLightStartsOnComponent"/> light when it spawns.
/// </summary>
public sealed partial class KsHandheldLightStartsOnSystem : EntitySystem
{
    [Dependency] private HandheldLightSystem _handheldLightSystem = default!;

    // After its cell is put in, which happens on map init too.
    [SubscribeLocalEvent(after: [typeof(ItemSlotsSystem), typeof(HandheldLightSystem)])]
    private void OnMapInit(Entity<KsHandheldLightStartsOnComponent> entity, ref MapInitEvent args)
    {
        if (TryComp<HandheldLightComponent>(entity.Owner, out var handheldLightComponent))
            _handheldLightSystem.TurnOn(entity.Owner, (entity.Owner, handheldLightComponent));
    }
}
