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
    ///     How long it keeps pushing one thing that does not budge before giving up on it, as something it cannot push.
    ///         Something wedged against a wall never clears the tile, and steering does not count an NPC handling an
    ///         obstacle as stuck. Pushing something along - down a corridor, tile by tile - is not giving up on: every
    ///         time it moves, the wait starts again.
    /// </summary>
    [DataField]
    public TimeSpan GiveUpAfter = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     What it is pushing now, if anything, where that was when it last moved, and since when it has not. A push
    ///         not kept up for a second is over: pushing the same thing again later starts afresh.
    /// </summary>
    [ViewVariables]
    public EntityUid? PushingUid;

    [ViewVariables]
    public System.Numerics.Vector2 PushingFrom;

    [ViewVariables]
    public TimeSpan PushingSince;

    [ViewVariables]
    public TimeSpan LastPushedAt;

    /// <summary>
    ///     What it could not push, and until when its paths go round it.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Unpushable = new();
}
