using Content.Shared._KS14.Ordnance.TTV;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Containers.ItemSlots;

namespace Content.Server._KS14.Ordnance.TTV;

public sealed class TTVSystem : SharedTTVSystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;

    protected override void OnTTVOpen(Entity<TTVComponent> ttv)
    {
        base.OnTTVOpen(ttv);
        EqualizeTTV(ttv.Owner, out _);
    }

    /// <summary>
    ///     Reacts and then equalises contents of every tank connected to a TTV.
    ///         This can lose gas due to inaccuracy.
    /// </summary>
    /// <returns>Whether the TTV was updated.</returns>
    public void EqualizeTTV(Entity<ItemSlotsComponent?> ttv, out GasMixture mixture)
    {
        if (!Resolve(ttv.Owner, ref ttv.Comp))
        {
            mixture = new();
            return;
        }

        GasMixture mergedMixture = new();
        List<GasMixture> affectedMixtures = new();

        foreach (var (_, slot) in ttv.Comp.Slots)
        {
            if (slot.Item is not { } itemUid || !GasTankQuery.TryComp(itemUid, out var itemGasTankComponent))
                continue;

            var airToMerge = itemGasTankComponent.Air;

            _atmosphereSystem.React(airToMerge, itemGasTankComponent);

            mergedMixture.Volume += airToMerge.Volume;
            _atmosphereSystem.Merge(mergedMixture, airToMerge);

            airToMerge.Clear();
            affectedMixtures.Add(airToMerge);
        }

        _atmosphereSystem.DivideInto(mergedMixture, affectedMixtures);
        mixture = mergedMixture;
    }
}
