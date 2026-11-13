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

    /// <summary>
    ///     Whether it cooks a frag (an explosive with a timer fuse) before throwing it: lights it, holds it, and throws it with
    ///         <see cref="CookMargin"/> of the fuse left on landing, so there is less time to get clear. Never a flash or smoke.
    ///         See <see cref="NpcGrenadeSystem.TryBeginThrow"/> for how a cut-short hold is made safe.
    /// </summary>
    [DataField]
    public bool Cooks;

    /// <summary>
    ///     How much of the fuse is left when a cooked grenade lands, in seconds. Never taken as less than
    ///         <see cref="NpcGrenadeSystem.MinCookMargin"/>.
    /// </summary>
    [DataField]
    public float CookMargin = 0.4f;

    /// <summary>
    ///     Each cook is up to this many seconds longer or shorter than that, at random.
    /// </summary>
    [DataField]
    public float CookSpread = 0.05f;
}
