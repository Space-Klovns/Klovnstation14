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

    /// <summary>
    ///     Recipients this beacon is currently linked to.
    ///     Rebuilt by the recipients themselves, so it is deliberately not a datafield.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> LinkedRecipientUids = [];
}
