using Content.Server._KS14.Llm.Tools;

namespace Content.Server._KS14.Llm.Fax;

/// <summary>
///     A fax machine whose line an LLM persona closed. Nothing it sends is answered any more, and once the
///         persona's reply has gone out it is destroyed - which is not how the persona was told it works.
/// </summary>
[RegisterComponent, Access(typeof(KsLlmFaxSystem), typeof(KsLlmCloseFaxLineToolEffect))]
public sealed partial class KsLlmClosedFaxLineComponent : Component
{
    /// <summary>
    ///     How long after the persona's turn ends the machine goes, so the reply has time to print.
    /// </summary>
    [DataField]
    public TimeSpan DestroyDelay = TimeSpan.FromSeconds(6);

    /// <summary>
    ///     Set when the turn that closed the line ends. Null until then.
    /// </summary>
    [DataField]
    public TimeSpan? DestroyAt;

    [DataField]
    public bool Destroyed;

    /// <summary>
    ///     Explosion prototype to set off on the machine, or null to just remove it.
    /// </summary>
    [DataField]
    public string? ExplosionType;

    [DataField]
    public float ExplosionTotalIntensity;

    [DataField]
    public float ExplosionSlope;

    [DataField]
    public float ExplosionMaxTileIntensity;
}
