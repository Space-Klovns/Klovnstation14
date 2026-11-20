using Content.Shared.NPC.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Perks.Support;

/// <summary>
///     Marked by Mark Target (<see cref="KsMarkTargetAbilityComponent"/>) until <see cref="EndTime"/>: takes
///         <see cref="DamageTakenMultiplier"/> times the damage, and is outlined in <see cref="OutlineColor"/> for anyone
///         sharing one of <see cref="Factions"/> (the marker's side). Added and taken off by the server.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
[Access(typeof(KsMarkedTargetSystem))]
public sealed partial class KsMarkedTargetComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public float DamageTakenMultiplier = 1f;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public Color OutlineColor = Color.Red;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public HashSet<ProtoId<NpcFactionPrototype>> Factions = new();

    /// <summary>
    ///     Who marked it, who sees the outline whatever their side.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? MarkerUid;
}
