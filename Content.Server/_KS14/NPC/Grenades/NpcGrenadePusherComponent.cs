namespace Content.Server._KS14.NPC.Grenades;

/// <summary>
///     Marks an NPC that goes in on the heels of a grenade from its squad, while whoever it was thrown at is still reeling.
///         See <see cref="NpcGrenadeSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NpcGrenadePusherComponent : Component
{
    /// <summary>
    ///     How long after the blast it is still worth going in.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(8);
}
