// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Linq;
using Content.Shared.Materials;
using Robust.Shared.Prototypes;

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
    ///     Returns amount of all materials contained in the entity
    ///         taking account the <see cref="ChargeByMaterialStorageComponent"/>'s
    ///         whitelist, if any. 
    /// </summary>
    public Dictionary<ProtoId<MaterialPrototype>, int> GetActiveStoredMaterials(Entity<ChargeByMaterialStorageComponent> entity)
    {
        if (entity.Comp.WhitelistedMaterials is not { } whitelistedMaterials)
            return _materialStorageSystem.GetStoredMaterials(entity.Owner, localOnly: entity.Comp.LocalOnly);

        if (whitelistedMaterials.Length == 0)
            return new();

        var storedMaterials = _materialStorageSystem.GetStoredMaterials(entity.Owner, localOnly: entity.Comp.LocalOnly);
        var activeStoredMaterials = new Dictionary<ProtoId<MaterialPrototype>, int>(whitelistedMaterials.Length);

        foreach (var whitelistedMaterial in whitelistedMaterials)
        {
            if (!storedMaterials.TryGetValue(whitelistedMaterial, out var materialAmount))
                continue;

            activeStoredMaterials[whitelistedMaterial] = materialAmount;
        }

        return activeStoredMaterials;
    }

    protected virtual void OnStartup(Entity<ChargeByMaterialStorageComponent> entity, ref ComponentStartup args)
    {
        entity.Comp.CachedStoredMaterials = GetActiveStoredMaterials(entity);
    }

    // Top 10 dictionary allocation spams of all time
    private void OnMaterialAmountChanged(Entity<ChargeByMaterialStorageComponent> entity, ref MaterialAmountChangedEvent args)
    {
        var activeStoredMaterials = GetActiveStoredMaterials(entity);

        var materialStorageComponent = Comp<MaterialStorageComponent>(entity.Owner);
        foreach (var (materialId, newMaterialAmount) in activeStoredMaterials)
        {
            var materialDelta = newMaterialAmount;

            if (entity.Comp.CachedStoredMaterials.TryGetValue(materialId, out var oldMaterialAmount))
                materialDelta -= oldMaterialAmount; // eq `materialDelta = newMaterialAmount - oldMaterialAmount`

            // prevent feedback loop
            if (materialDelta <= 0)
                continue;

            ChangeCharge(entity, materialDelta * entity.Comp.GainRatio);

            if (entity.Comp.ConsumeAddedMaterials)
                _materialStorageSystem.TryChangeMaterialAmount(
                    entity.Owner,
                    materialId,
                    -materialDelta,
                    component: materialStorageComponent,
                    localOnly: entity.Comp.LocalOnly
                );
        }

        entity.Comp.CachedStoredMaterials = new(materialStorageComponent.Storage);
    }

    // Empty on client because nothing ever happens on client
    // TODO LCDC: PredictedBatteryComponent when apstrim merge
    protected abstract void ChangeCharge(Entity<ChargeByMaterialStorageComponent> entity, float charge);
}
