namespace Content.Client._KS14.Storage.ClosetDoor;

/// <summary>
///     The world rotation something in a closet had just before it went in. Going into a container zeroes an entity's
///         rotation, so without it, everything drawn inside a closing closet would face south.
/// </summary>
[RegisterComponent]
[Access(typeof(KsClosetDoorSystem))]
public sealed partial class KsClosetContentFacingComponent : Component
{
    [ViewVariables]
    public Angle WorldRotation;
}
