using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Grenades;

/// <summary>
///     On a grenade an NPC has thrown, until it goes off. See <see cref="NpcGrenadeSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcGrenadeSystem))]
public sealed partial class NpcThrownGrenadeComponent : Component
{
    [ViewVariables]
    public EntityUid ThrowerUid;

    /// <summary>
    ///     Where it was thrown at, which is where the hostile is believed to be. Belongs to its grid.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates Coordinates;
}
