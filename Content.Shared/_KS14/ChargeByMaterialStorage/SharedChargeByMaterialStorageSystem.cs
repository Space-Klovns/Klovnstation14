using Content.Shared.Materials;

namespace Content.Shared._KS14.ChargeByMaterialStorage;

/// <summary>
///     Handles <see cref="ChargeByMaterialStorageComponent"/>. 
/// </summary>
public abstract class SharedChargeByMaterialStorageSystem : EntitySystem
{
    [Dependency] private readonly SharedMaterialStorageSystem _materialStorageSystem = default!;

    public override void Initialize()
    {
        base.Initialize();


        SubscribeLocalEvent<ChargeByMaterialStorageComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ChargeByMaterialStorageComponent, MaterialAmountChangedEvent>(OnMaterialAmountChanged);
    }

    /// <summary>
    ///     Returns amount of material contained in the entity
    ///         taking account the <see cref="ChargeByMaterialStorageComponent"/>'s
    ///         whitelist, if any. 
    /// </summary>
    public int GetActiveStoredMaterialAmount(Entity<ChargeByMaterialStorageComponent> entity)
    {
        if (entity.Comp.WhitelistedMaterials is not { } whitelistedMaterials)
            return _materialStorageSystem.GetTotalMaterialAmount(entity.Owner);

        var activeStoredMaterialAmount = 0;
        var storedMaterials = _materialStorageSystem.GetStoredMaterials(entity.Owner);

        for (var i = 0; i < entity.Comp.WhitelistedMaterials.Length; i++)
        {
            var whitelistedMaterial = entity.Comp.WhitelistedMaterials[i];
            if (!storedMaterials.TryGetValue(whitelistedMaterial, out var materialAmount))
                continue;

            activeStoredMaterialAmount += materialAmount;
        }

        return activeStoredMaterialAmount;
    }

    private void OnStartup(Entity<ChargeByMaterialStorageComponent> entity, ref ComponentStartup args)
    {
        entity.Comp.CachedTotalMaterialAmount = GetActiveStoredMaterialAmount(entity);
    }

    private void OnMaterialAmountChanged(Entity<ChargeByMaterialStorageComponent> entity, ref MaterialAmountChangedEvent args)
    {
        // Amount of material gained/lost
        var materialDelta = GetActiveStoredMaterialAmount(entity) - entity.Comp.CachedTotalMaterialAmount;
        var powerDelta = 0f;

        if (materialDelta > 0) // Gain
        {
            if (entity.Comp.GainRatio == 0f)
                return;

            powerDelta = materialDelta * entity.Comp.GainRatio;
        }
        else if (materialDelta < 0) // Loss
        {
            if (entity.Comp.LossRatio == 0f)
                return;

            powerDelta = materialDelta * entity.Comp.LossRatio;
        }
        else // Material delta of 0
            return;

        AddCharge(entity, powerDelta);
    }

    // Empty on client because nothing ever happens on client
    // TODO LCDC: PredictedBatteryComponent when apstrim merge
    protected abstract void AddCharge(Entity<ChargeByMaterialStorageComponent> entity, float charge);
}
