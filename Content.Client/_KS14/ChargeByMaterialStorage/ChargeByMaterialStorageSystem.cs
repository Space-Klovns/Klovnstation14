// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._KS14.ChargeByMaterialStorage;

namespace Content.Client._KS14.ChargeByMaterialStorage;

/// <inheritdoc/>
public sealed class ChargeByMaterialStorageSystem : SharedChargeByMaterialStorageSystem
{
    protected override void ChangeCharge(Entity<ChargeByMaterialStorageComponent> entity, float charge) { }
}
