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
    ///     Currently does not support being changed during runtime. You should
    ///         only use this if you really need it.
    /// </summary>
    [DataField]
    [Access(typeof(SharedChargeByMaterialStorageSystem), Other = AccessPermissions.Read)]
    public ProtoId<MaterialPrototype>[]? WhitelistedMaterials = null;

    [Access(typeof(SharedChargeByMaterialStorageSystem))]
    public int CachedTotalMaterialAmount = default;

    /// <summary>
    ///     Amount of energy gained, in joules, per unit (cm³) of
    ///         material added to this entity.
    /// 
    ///     If zero, nothing happens when material is added
    ///         to this entity.
    /// </summary>
    [DataField]
    public float GainRatio = 1f;

    /// <summary>
    ///     Amount of energy added [sic], in joules, per unit (cm³) of
    ///         material taken from this entity.
    /// 
    ///     If zero, nothing happens when material is taken
    ///         from this entity. If you want the entity to lose
    ///         energy when losing material, then this should be
    ///         negative, and vice versa.
    /// </summary>
    [DataField]
    public float LossRatio = 0f;
}

