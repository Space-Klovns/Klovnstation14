namespace Content.Shared._KS14.NPC.Passages;

/// <summary>
///     Marks something NPCs can take to get to another map: a ladder, a portal. Where it leads, and how it is taken,
///         are for whatever system owns the thing to say; see <c>KsNpcPassageSystem</c> on the server.
/// </summary>
[RegisterComponent]
public sealed partial class KsNpcPassageComponent : Component
{
    /// <summary>
    ///     How close an NPC gets to it before it takes it. Next to nothing for one that is walked into, which takes the
    ///         NPC before it gets this close. Over a tile for one that may stand on a wall and be used from beside it.
    /// </summary>
    [DataField]
    public float Range = 0.05f;
}
