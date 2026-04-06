using Content.Server.Cargo.Systems;
using Content.Shared.Cargo;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem : SharedGunSystem
{
    [Dependency] private readonly PricingSystem _pricing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BallisticAmmoProviderComponent, PriceCalculationEvent>(OnBallisticPrice);
    }

    private void OnBallisticPrice(Entity<BallisticAmmoProviderComponent> ent, ref PriceCalculationEvent args)
    {
        if (string.IsNullOrEmpty(ent.Comp.Proto) || ent.Comp.UnspawnedCount == 0)
            return;

        if (!ProtoManager.TryIndex<EntityPrototype>(ent.Comp.Proto, out var proto))
        {
            Log.Error($"Unable to find fill prototype for price on {ent.Comp.Proto} on {ToPrettyString(ent)}");
            return;
        }

        // Probably good enough for most.
        var price = _pricing.GetEstimatedPrice(proto);
        args.Price += price * ent.Comp.UnspawnedCount;
    }

    // Trauma - moved almost everything to shared

    protected override void Popup(string message, EntityUid? uid, EntityUid? user) { }

    protected override void CreateEffect(EntityUid gunUid, MuzzleFlashEvent message, EntityUid? user = null)
    {
        var filter = Filter.Pvs(gunUid, entityManager: EntityManager);

        if (TryComp<ActorComponent>(user, out var actor))
            filter.RemovePlayer(actor.PlayerSession);

        RaiseNetworkEvent(message, filter);
    }

    // Trauma - predicted PlayImpactSound
}
