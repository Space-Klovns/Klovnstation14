namespace Content.Server._KS14.NPC.Pushing;

/// <summary>
///     The loose things an NPC walked up to and found it could not push. Its paths go round them for a while, rather than
///         straight back into them. Added when first needed; remembered things are dropped once they expire, when the
///         next is added: nothing ticks them.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcPushSystem))]
public sealed partial class NpcPusherComponent : Component
{
    /// <summary>
    ///     How long something it could not push stays out of its paths.
    /// </summary>
    [DataField]
    public TimeSpan ForgetAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     What it could not push, and until when its paths go round it.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Unpushable = new();
}
