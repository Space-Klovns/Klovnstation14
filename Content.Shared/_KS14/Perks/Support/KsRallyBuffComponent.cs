using Content.Shared.Alert;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Perks.Support;

/// <summary>
///     Rallied (<see cref="KsRallyAbilityComponent"/>) until <see cref="EndTime"/>: moves, fires and deals gun damage
///         at these multiples. Added and taken off by the server; read on both sides (<see cref="KsRallyBuffSystem"/>),
///         so speed and fire rate are predicted.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
[Access(typeof(KsRallyBuffSystem))]
public sealed partial class KsRallyBuffComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public float SpeedMultiplier = 1f;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public float FireRateMultiplier = 1f;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public float DamageMultiplier = 1f;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    [ViewVariables(VVAccess.ReadOnly)]
    public ProtoId<AlertPrototype>? Alert;

    [ViewVariables(VVAccess.ReadOnly)]
    public SoundSpecifier? EndSound;
}
