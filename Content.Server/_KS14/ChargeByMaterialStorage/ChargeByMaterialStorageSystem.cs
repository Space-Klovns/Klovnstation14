// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._KS14.ChargeByMaterialStorage;
using Content.Shared.Materials;

namespace Content.Server._KS14.ChargeByMaterialStorage;

/// <inheritdoc/>
public sealed class ChargeByMaterialStorageSystem : SharedChargeByMaterialStorageSystem
{
    [Dependency] private readonly BatterySystem _batterySystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChargeByMaterialStorageComponent, ChargeChangedEvent>(OnChargeChanged);
    }

    protected override void OnStartup(Entity<ChargeByMaterialStorageComponent> entity, ref ComponentStartup args)
    {
        base.OnStartup(entity, ref args);

        if (!entity.Comp.AdjustStorageLimitAccordingToBatteryCharge ||
            !TryComp<MaterialStorageComponent>(entity, out var materialStorageComponent) ||
            !TryComp<BatteryComponent>(entity, out var batteryComponent))
            return;

        materialStorageComponent.StorageLimit = (int)MathF.Ceiling((batteryComponent.MaxCharge - batteryComponent.CurrentCharge) / entity.Comp.GainRatio);
        Dirty(entity.Owner, materialStorageComponent);
    }

    private void OnChargeChanged(Entity<ChargeByMaterialStorageComponent> entity, ref ChargeChangedEvent args)
    {
        if (!entity.Comp.AdjustStorageLimitAccordingToBatteryCharge)
            return;

        if (!TryComp<MaterialStorageComponent>(entity, out var materialStorageComponent))
            return;

        var remainingCharge = args.MaxCharge - args.Charge;
        materialStorageComponent.StorageLimit = (int)MathF.Ceiling(remainingCharge / entity.Comp.GainRatio);
        Dirty(entity.Owner, materialStorageComponent);
    }

    protected override void ChangeCharge(Entity<ChargeByMaterialStorageComponent> entity, float charge)
    {
        if (!TryComp<BatteryComponent>(entity, out var batteryComponent))
            return;

        if (batteryComponent.CurrentCharge + charge > batteryComponent.MaxCharge)
            return;

        _batterySystem.ChangeCharge(entity.Owner, charge, battery: batteryComponent);
    }
}
