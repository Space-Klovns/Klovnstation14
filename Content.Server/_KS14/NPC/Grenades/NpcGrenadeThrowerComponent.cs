namespace Content.Server._KS14.NPC.Grenades;

/// <summary>
///     Marks an NPC that throws the grenades it carries at hostiles it has lost sight of. See
///         <see cref="NpcGrenadeSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NpcGrenadeThrowerComponent : Component
{
    /// <summary>
    ///     Whether it tells its squad when one of its grenades goes off, for those that go in on it
    ///         (<see cref="NpcGrenadePusherComponent"/>).
    /// </summary>
    [DataField]
    public bool CallsOutBlasts = true;
}
