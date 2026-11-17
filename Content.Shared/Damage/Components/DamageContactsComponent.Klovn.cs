using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Damage.Components;

public sealed partial class DamageContactsComponent
{
    /// <summary>
    ///     If true, whoever touches this is only damaged while moving: standing still in it is safe, and the first step
    ///         after standing still hurts at once if a damage tick is due.
    /// </summary>
    /// <seealso cref="Content.Shared.Damage.Systems.DamageContactsSystem.KsMovingSpeed"/>
    [DataField]
    public bool OnlyWhileMoving;

    /// <summary>
    ///     Sources with the same group never hurt the same entity at once: touching several of them hurts like touching
    ///         one, whichever was touched first, and when that one is left the next takes over. Sources of different
    ///         groups each hurt for themselves. Null is no group of its own: of all the sources without one that an
    ///         entity touches, one hurts it, as upstream has it.
    /// </summary>
    [DataField]
    public string? ExclusiveGroup;

    /// <summary>
    ///     If set, this wears out as it hurts: for each point of damage it actually deals, after the victim's armour
    ///         and resistances, it takes this many points of <see cref="SelfDamageType"/> itself, past its own
    ///         resistances. So a source that is destroyed at D damage breaks after dealing D divided by this in all.
    ///         Null, the default, never wears.
    /// </summary>
    [DataField]
    public float? SelfDamageRatio;

    /// <summary>
    ///     The type of the damage <see cref="SelfDamageRatio"/> does to this. One its damage container takes.
    /// </summary>
    [DataField]
    public ProtoId<DamageTypePrototype> SelfDamageType = "Structural";
}
