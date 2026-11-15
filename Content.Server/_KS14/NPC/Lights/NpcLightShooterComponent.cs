namespace Content.Server._KS14.NPC.Lights;

/// <summary>
///     Marks an NPC that shoots out lights to fight in the dark it sees in: the lights of a room it is about to go into, and
///         the light that shows a hostile who cannot see in the dark. It never looks for darkness, nor prefers dark
///         positions: it only darkens where the hostile is, or where it is about to go. See
///         <see cref="NpcLightShooterSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcLightShooterSystem))]
public sealed partial class NpcLightShooterComponent : Component
{
    /// <summary>
    ///     Whether it shoots out the lit lights of the room its squad is staging at the door of, which it can see from where it
    ///         stands, before going in.
    /// </summary>
    [DataField]
    public bool ShootsWhileStaging = true;

    /// <summary>
    ///     Whether it shoots out the light on a hostile in a fight, when the hostile cannot see in the dark and there is no
    ///         shot at the hostile itself.
    /// </summary>
    [DataField]
    public bool ShootsInFights = true;

    /// <summary>
    ///     The farthest a light can be from the shooter to be shot at, in tiles.
    /// </summary>
    [DataField]
    public float Range = 10f;

    /// <summary>
    ///     How lit a visible hostile has to look, from 0 to 1, for the light on it to be worth a shot. A hostile that is not
    ///         in sight is taken as lit if a light's reach covers where it is believed to be.
    /// </summary>
    [DataField]
    public float FightLightLevel = 0.25f;

    /// <summary>
    ///     How long after a failed or abandoned attempt, in seconds, before it tries another.
    /// </summary>
    [DataField]
    public float Cooldown = 8f;

    /// <summary>
    ///     How long after a light has gone out, in seconds, before it goes for the next: a room has more than one, and the
    ///         squad does not wait long at its door.
    /// </summary>
    [DataField]
    public float SuccessCooldown = 0.4f;

    /// <summary>
    ///     The most one attempt takes, in seconds: aiming, locking on and firing until the light goes out. After that it is
    ///         given up on.
    /// </summary>
    [DataField]
    public float TaskTime = 2.5f;

    /// <summary>
    ///     How long a hostile out of sight is still counted for a fight, in seconds since it was last seen or called out.
    /// </summary>
    [DataField]
    public float LostContactAge = 5f;

    /// <summary>
    ///     How much of the dark a hostile has to see through for its lights to be left alone, from 0 to 1.
    /// </summary>
    [DataField]
    public float HostileDarkVision = 0.5f;

    /// <summary>
    ///     How often it looks for a light, in seconds.
    /// </summary>
    [DataField]
    public float UpdateInterval = 0.3f;

    #region State

    [ViewVariables]
    public EntityUid? Target;

    [ViewVariables]
    public NpcLightShotReason Reason;

    [ViewVariables]
    public TimeSpan ExpiresAt;

    [ViewVariables]
    public TimeSpan NextPickAt;

    public TimeSpan NextUpdate;

    #endregion
}

/// <summary>
///     Why an NPC is shooting a light.
/// </summary>
public enum NpcLightShotReason : byte
{
    None,

    /// <summary>
    ///     A light of the room it is staging at the door of.
    /// </summary>
    Staging,

    /// <summary>
    ///     A light on a hostile that cannot see in the dark.
    /// </summary>
    Fight,
}
