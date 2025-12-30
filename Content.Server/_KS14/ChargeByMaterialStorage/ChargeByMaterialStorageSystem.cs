// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MPL-2.0

using Content.Server.Power.EntitySystems;
using Content.Shared._KS14.ChargeByMaterialStorage;

namespace Content.Server._KS14.ChargeByMaterialStorage;

/// <inheritdoc/>
public sealed class ChargeByMaterialStorageSystem : SharedChargeByMaterialStorageSystem
{
    [Dependency] private readonly BatterySystem _batterySystem = default!;

    protected override void ChangeCharge(Entity<ChargeByMaterialStorageComponent> entity, float charge)
    {
        _batterySystem.ChangeCharge(entity.Owner, charge);
    }
}
