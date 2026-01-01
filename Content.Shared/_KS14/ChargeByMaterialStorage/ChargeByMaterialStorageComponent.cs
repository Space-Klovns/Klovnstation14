// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Materials;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.ChargeByMaterialStorage;

/// <summary>
///     Charges an entity's battery when material is inserted
///         into the entity's <see cref="MaterialStorageComponent">.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ChargeByMaterialStorageComponent : Component
{
    /// <summary>
    ///     If null, all materials in the entity's <see cref="MaterialStorageComponent"/>
    ///         can be used to charge. Otherwise if this is notnull, then
    ///         only the specified materials can be used to charge.
    /// 
    ///     Currently does not support being changed during after the component is started. You should
    ///         only use this if you really need it.
    /// </summary>
    [DataField]
    [Access(typeof(SharedChargeByMaterialStorageSystem), Other = AccessPermissions.Read)]
    public ProtoId<MaterialPrototype>[]? WhitelistedMaterials = null;

    [Access(typeof(SharedChargeByMaterialStorageSystem))]
    public Dictionary<ProtoId<MaterialPrototype>, int> CachedStoredMaterials = new();

    /// <summary>
    ///     If true, then the entity's <see cref="MaterialStorageComponent.StorageLimit">
    ///         will be adjusted 
    /// </summary>
    [DataField]
    public bool AdjustStorageLimitAccordingToBatteryCharge = false;

    /// <summary>
    ///     Amount of energy gained, in joules, per unit (cm³) of
    ///         material added to this entity.
    /// 
    ///     If zero, nothing happens when material is added
    ///         to this entity.
    /// 
    ///     Currently does not support being changed during after the component is started.
    /// </summary>
    [DataField]
    [Access(typeof(SharedChargeByMaterialStorageSystem), Other = AccessPermissions.Read)]
    public float GainRatio = 1f;

    /// <summary>
    ///     When material is added, is it instantly deleted?
    /// </summary>
    [DataField]
    public bool ConsumeAddedMaterials = true;

    /// <summary>
    ///     Does this count only material added *directly* to the entity,
    ///         or also include material indirectly added to the entity (ore silos, etc.)
    /// 
    ///     Currently does not support being changed during after the component is started.
    /// </summary>
    [DataField]
    [Access(typeof(SharedChargeByMaterialStorageSystem))]
    public bool LocalOnly = true;
}

