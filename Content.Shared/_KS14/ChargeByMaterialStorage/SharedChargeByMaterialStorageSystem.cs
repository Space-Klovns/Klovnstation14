// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Materials;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.ChargeByMaterialStorage;

/// <summary>
///     Handles <see cref="ChargeByMaterialStorageComponent"/>. 
/// </summary>
public sealed class SharedChargeByMaterialStorageSystem : EntitySystem
{
    [Dependency] private readonly SharedMaterialStorageSystem _materialStorageSystem = default!;
    [Dependency] private readonly SharedBatterySystem _batterySystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChargeByMaterialStorageComponent, ComponentStartup>(OnStartup);
        // Necessary as batterysystem updates charge of batteries to starting value at mapinit, and we depend on that, so make it be after
        SubscribeLocalEvent<ChargeByMaterialStorageComponent, MapInitEvent>(OnMapInit, after: new[] { typeof(SharedBatterySystem) });

        SubscribeLocalEvent<ChargeByMaterialStorageComponent, ChargeChangedEvent>(OnChargeChanged);
        SubscribeLocalEvent<ChargeByMaterialStorageComponent, MaterialAmountChangedEvent>(OnMaterialAmountChanged);
    }

    private void OnChargeChanged(Entity<ChargeByMaterialStorageComponent> entity, ref ChargeChangedEvent args)
    {
        if (!entity.Comp.AdjustStorageLimitAccordingToBatteryCharge)
            return;

        if (!TryComp<MaterialStorageComponent>(entity, out var materialStorageComponent))
            return;

        var remainingCharge = args.MaxCharge - args.CurrentCharge;
        materialStorageComponent.StorageLimit = (int)MathF.Ceiling(remainingCharge / entity.Comp.GainRatio);
        Dirty(entity.Owner, materialStorageComponent);
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

    private void OnStartup(Entity<ChargeByMaterialStorageComponent> entity, ref ComponentStartup args)
    {
        entity.Comp.CachedStoredMaterials = GetActiveStoredMaterials(entity);
    }

    private void OnMapInit(Entity<ChargeByMaterialStorageComponent> entity, ref MapInitEvent args)
    {
        if (!entity.Comp.AdjustStorageLimitAccordingToBatteryCharge ||
            !TryComp<MaterialStorageComponent>(entity, out var materialStorageComponent) ||
            !TryComp<BatteryComponent>(entity, out var batteryComponent))
            return;

        materialStorageComponent.StorageLimit = (int)MathF.Ceiling((batteryComponent.MaxCharge - _batterySystem.GetCharge((entity.Owner, batteryComponent))) / entity.Comp.GainRatio);
        Dirty(entity.Owner, materialStorageComponent);
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

    private void ChangeCharge(Entity<ChargeByMaterialStorageComponent> entity, float addedCharge)
    {
        if (!TryComp<BatteryComponent>(entity, out var batteryComponent))
            return;

        var batteryEntity = new Entity<BatteryComponent?>(entity.Owner, batteryComponent);
        if (_batterySystem.GetCharge(batteryEntity) + addedCharge > batteryComponent.MaxCharge)
            return;

        _batterySystem.ChangeCharge(batteryEntity, addedCharge);
    }
}
