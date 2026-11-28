using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.WallHealer;

/// <summary>
///     One reserve of treatment held by a <see cref="KsWallHealerComponent"/>. It drains as it treats patients and
///         recharges while nobody is using the healer. Each kind of pool decides what it treats; the amount it treats
///         is always counted in the same units as the pool.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class KsWallHealerPool
{
    /// <summary>
    ///     The most this pool holds.
    /// </summary>
    [DataField(required: true)]
    public float Capacity;

    /// <summary>
    ///     The sprite layer showing how full this pool is, by its map key, if any.
    /// </summary>
    [DataField]
    public string? Layer;
}

/// <summary>
///     Heals any of the given damage types, split between them by how much of each the patient has.
/// </summary>
public sealed partial class KsWallHealerDamagePool : KsWallHealerPool
{
    [DataField(required: true)]
    public List<ProtoId<DamageTypePrototype>> DamageTypes = new();
}

/// <summary>
///     Restores blood, in units of blood, up to a fraction of the patient's normal blood volume.
/// </summary>
public sealed partial class KsWallHealerBloodPool : KsWallHealerPool
{
    /// <summary>
    ///     The blood level, as a fraction of normal volume, that this pool restores up to and no further.
    /// </summary>
    [DataField]
    public float TargetBloodLevel = 0.6f;
}
