using Content.Shared.Atmos;

namespace Content.Server._KS14.Fluidmos.Devices;

/// <summary>
///     A pipe fitting with a small tank that beakers, jugs and plumbing can fill and drain. Injecting, it feeds its tank
///         into the pipe as liquid; extracting, it draws the pipe's liquid (and only its liquid) into the tank, every
///         reagent's data kept.
/// </summary>
[RegisterComponent, Access(typeof(KsFluidPortSystem))]
public sealed partial class KsFluidPortComponent : Component
{
    [DataField]
    public string PipeName = "pipe";

    [DataField]
    public string SolutionName = "fluidport";

    [DataField]
    public KsFluidPortMode Mode = KsFluidPortMode.Inject;

    /// <summary>
    ///     Units per second moved either way.
    /// </summary>
    [DataField]
    public float Rate = 10f;

    /// <summary>
    ///     Injection stops at this pipe pressure, in kPa, as a pump's would.
    /// </summary>
    [DataField]
    public float MaxPressure = Atmospherics.MaxOutputPressure;
}

public enum KsFluidPortMode : byte
{
    Inject,
    Extract,
}
