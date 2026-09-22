// KS14: added in this fork
using Content.Server._KS14.SaturnClouds;
using Content.Server.Salvage.Magnet;
using Content.Shared.Salvage.Magnet;

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    private bool KsTryCreateSaturnCloudOffers(Entity<SalvageMagnetDataComponent> data)
    {
        var cloudMapQuery = EntityQueryEnumerator<SaturnCloudMapComponent>();
        if (!cloudMapQuery.MoveNext(out _, out _))
            return false;

        for (var i = 0; i < data.Comp.OfferCount; i++)
        {
            int seed;
            do
            {
                seed = _random.Next();
            } while (GetSalvageOffering(seed) is not SalvageOffering);

            data.Comp.Offered.Add(seed);
        }

        data.Comp.NextOffer = _timing.CurTime + data.Comp.OfferCooldown;
        UpdateMagnetUIs(data);
        return true;
    }
}
