using Robust.Shared.Serialization;

namespace Content.Shared._KS14.NPC;

/// <summary>
///     What an NPC believes about one hostile. Kept by the server's NpcPerceptionSystem;
///         shared so the squad debug overlay can draw it.
/// </summary>
[Serializable, NetSerializable]
public enum NpcContactState : byte
{
    /// <summary>
    ///     In sight right now.
    /// </summary>
    Visible,

    /// <summary>
    ///     Was in sight, and no longer is. Remembered where it was last seen.
    /// </summary>
    Lost,

    /// <summary>
    ///     Seen getting into its container, and believed to still be in there.
    /// </summary>
    Concealed,

    /// <summary>
    ///     Vanished in plain sight next to its container, so probably hid in it - nobody saw.
    /// </summary>
    Suspected,

    /// <summary>
    ///     Never seen by this NPC (or not lately): a squadmate called out where it is.
    /// </summary>
    Reported,
}
