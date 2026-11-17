using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.SupplyPod.Passengers;

/// <summary>
///     A supply pod that arrives carrying humanoids, spawned when the pod is and boarded asleep, so that they can't act
///         until it opens.
/// </summary>
[RegisterComponent, Access(typeof(KsSupplyPodPassengerSystem))]
public sealed partial class KsSupplyPodHumanoidPassengersComponent : Component
{
    /// <summary>
    ///     One humanoid for each entry.
    /// </summary>
    [DataField]
    public List<ProtoId<RandomHumanoidSettingsPrototype>> Settings = [];

    /// <summary>
    ///     Name a passenger gets when its settings don't randomise one.
    /// </summary>
    [DataField]
    public LocId Name = "ks-supplypod-passenger-name";
}
