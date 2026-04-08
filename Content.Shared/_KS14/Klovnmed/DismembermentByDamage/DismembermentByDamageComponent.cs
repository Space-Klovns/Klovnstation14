using Content.Shared.Body;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Klovnmed.DismembermentByDamage;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(DismembermentByDamageSystem))]
public sealed partial class DismembermentByDamageComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<DamageTypePrototype> DamageProtoId;

    /// <summary>
    ///     Accumulated damage threshold for something to ever happen.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float DeltaDamageThreshold = 20f;

    /// <summary>
    ///     Accumulated damage threshold for something to ever happen.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float AccumulatedDamageThreshold = 220f;

    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public HashSet<ProtoId<OrganCategoryPrototype>> OrganCategories;

    public float LastAccumulatedDamage = 0f;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan LastUpdate = TimeSpan.MinValue;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextFuckup = TimeSpan.MinValue;
}
