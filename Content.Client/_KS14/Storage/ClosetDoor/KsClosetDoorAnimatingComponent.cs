namespace Content.Client._KS14.Storage.ClosetDoor;

/// <summary>
///     Marks a <see cref="KsClosetDoorVisualsComponent"/> whose door is mid-swing, so only those are checked for the
///         end of their swing each frame.
/// </summary>
[RegisterComponent]
[Access(typeof(KsClosetDoorSystem))]
public sealed partial class KsClosetDoorAnimatingComponent : Component
{
}
