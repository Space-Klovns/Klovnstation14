using Content.Server.Power.EntitySystems;
using Content.Shared._KS14.ChargeByMaterialStorage;

namespace Content.Server._KS14.ChargeByMaterialStorage;

/// <inheritdoc/>
public sealed class ChargeByMaterialStorageSystem : SharedChargeByMaterialStorageSystem
{
    [Dependency] private readonly BatterySystem _batterySystem = default!;

    protected override void AddCharge(Entity<ChargeByMaterialStorageComponent> entity, float charge)
    {
        _batterySystem.TryUseCharge(entity.Owner, charge);
    }
}
