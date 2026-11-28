using Content.Server._KS14.Cultivation.Systems;

namespace Content.Server._KS14.Cultivation.Components;

/// <summary>
///     On a master's mind: whom they have taken on as disciples, and whom they have named heir.
/// </summary>
[RegisterComponent, Access(typeof(InheritanceSystem))]
public sealed partial class CultivationLineageComponent : Component
{
    /// <summary>
    ///     Minds of the disciples they've taken on: body disciples, and whoever their jade slips awakened.
    /// </summary>
    [DataField]
    public HashSet<EntityUid> Disciples = new();

    /// <summary>
    ///     The mind that inherits their cultivation when they die.
    /// </summary>
    [DataField]
    public EntityUid? Heir;
}

/// <summary>
///     On a jade slip: the mind of the master who wrote it, so whoever it awakens becomes their disciple.
/// </summary>
[RegisterComponent, Access(typeof(InheritanceSystem))]
public sealed partial class JadeSlipTeacherComponent : Component
{
    [DataField]
    public EntityUid? TeacherMind;
}

/// <summary>
///     A master's cultivation lives on in them: their next breakthrough or Tribulation of Flesh cannot fail.
/// </summary>
[RegisterComponent, Access(typeof(InheritanceSystem))]
public sealed partial class InheritanceBlessingComponent : Component;
