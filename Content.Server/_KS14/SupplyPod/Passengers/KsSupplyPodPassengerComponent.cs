namespace Content.Server._KS14.SupplyPod.Passengers;

/// <summary>
///     An NPC riding a supply pod to where it lands. A pod sits at its destination on the server for the whole of its
///         fall, and only the client draws it coming down, so a passenger that could think would see the enemies around
///         its landing zone and open fire from inside the pod. It sleeps until the pod lets it out.
/// </summary>
[RegisterComponent, Access(typeof(KsSupplyPodPassengerSystem))]
public sealed partial class KsSupplyPodPassengerComponent : Component
{
    /// <summary>
    ///     The pod carrying it.
    /// </summary>
    [ViewVariables]
    public EntityUid PodUid;
}
