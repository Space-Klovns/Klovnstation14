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

    /// <summary>
    ///     Ordered after the item slots, which put the light's cell in on map init too: a light with no cell won't
    ///         switch on.
    /// </summary>
    [SubscribeLocalEvent(after: [typeof(ItemSlotsSystem), typeof(HandheldLightSystem)])]
    private void OnMapInit(Entity<KsHandheldLightStartsOnComponent> entity, ref MapInitEvent args)
    {
        if (TryComp<HandheldLightComponent>(entity.Owner, out var handheldLightComponent))
            _handheldLightSystem.TurnOn(entity.Owner, (entity.Owner, handheldLightComponent));
    }
}
