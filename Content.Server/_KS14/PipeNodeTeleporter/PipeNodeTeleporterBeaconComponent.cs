namespace Content.Server._KS14.PipeNodeTeleporter;

[RegisterComponent]
public sealed partial class PipeNodeTeleporterBeaconComponent : Component
{
    /// <summary>
    ///     Name of the node on this entity that will be connected to the
    ///         recipient.
    /// </summary>
    [DataField(readOnly: true)]
    public string NodeName = "tele_bec";

    [DataField]
    public HashSet<EntityUid> LinkedRecipientUids = [];
}
