namespace Content.Server._KS14.Light;

/// <summary>
///     Keeps the server's copy of an expendable light's (flare, glowstick, torch) point light in step with what
///         clients actually draw, so that server-side light levels - NPCs noticing who stands in a flare's glow -
///         see the light at all. See <see cref="KsExpendableLightMirrorSystem"/>.
/// </summary>
/// <remarks>
///     Expendable lights are <c>netsync: false</c>: the client switches the light on and animates its radius and
///         energy itself, through client-only light behaviours, and the server's copy stays disabled at whatever the
///         prototype declared (a flare's is radius 1, which lights nothing). Because the component is not synced,
///         what this writes to the server's copy never reaches a client. The values here approximate the client's
///         animation rather than reproduce it.
/// </remarks>
[RegisterComponent]
[Access(typeof(KsExpendableLightMirrorSystem))]
public sealed partial class KsExpendableLightMirrorComponent : Component
{
    [DataField]
    public float LitRadius = 5f;

    [DataField]
    public float LitEnergy = 3f;

    [DataField]
    public float FadingRadius = 3f;

    [DataField]
    public float FadingEnergy = 1.5f;
}
