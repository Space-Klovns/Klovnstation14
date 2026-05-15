namespace Content.Server._KS14.PipeNodeTeleporter;

[RegisterComponent]
public sealed partial class PipeNodeTeleporterRecipientComponent : Component
{
    /// <summary>
    ///     Name of the node on this entity that the beacons will be connected to.
    /// </summary>
    [DataField(readOnly: true)]
    public string NodeName = "tele_rec";

    [DataField]
    public HashSet<EntityUid> LinkedBeaconUids = [];
}
