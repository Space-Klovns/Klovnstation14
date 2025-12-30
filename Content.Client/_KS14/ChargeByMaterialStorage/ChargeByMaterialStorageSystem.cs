using Content.Shared._KS14.ChargeByMaterialStorage;

namespace Content.Client._KS14.ChargeByMaterialStorage;

/// <inheritdoc/>
public sealed class ChargeByMaterialStorageSystem : SharedChargeByMaterialStorageSystem
{
    protected override void ChangeCharge(Entity<ChargeByMaterialStorageComponent> entity, float charge) { }
}
