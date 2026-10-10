using Content.Server._KS14.Llm.Tools;
using Content.Shared.Database;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Llm.Prototypes;

/// <summary>
///     One action the LLM may take in the game. The parameter list here is the single source of both the schema
///         the model is shown and the validation its calls go through before <see cref="Effect"/> ever runs.
/// </summary>
[Prototype]
public sealed partial class KsLlmToolPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     The function name the model sees and calls, e.g. <c>make_announcement</c>.
    /// </summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>
    ///     What the tool does and when to use it. Model-facing.
    /// </summary>
    [DataField(required: true)]
    public string Description = string.Empty;

    [DataField]
    public List<KsLlmToolParameter> Parameters = new();

    /// <summary>
    ///     How many times per round the tool may succeed. Null for unlimited. Only successful calls count.
    /// </summary>
    [DataField]
    public int? MaxUsesPerRound;

    /// <summary>
    ///     When non-empty, the tool only runs for a fax bearing at least one of these stamps (by
    ///         <see cref="Content.Shared.Paper.StampDisplayInfo.StampedName"/>). Enforced in code, and written into
    ///         the description the model sees, so this list is the one place that says who may use the tool.
    /// </summary>
    [DataField]
    public List<string> RequiredStamps = new();

    /// <summary>
    ///     Minimum time between successful calls.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.Zero;

    /// <summary>
    ///     Impact of the admin log entry written for every call of this tool.
    /// </summary>
    [DataField]
    public LogImpact LogImpact = LogImpact.Medium;

    /// <summary>
    ///     Whether a successful call is also announced in admin chat.
    /// </summary>
    [DataField]
    public bool NotifyAdmins;

    [DataField(required: true)]
    public KsLlmToolEffect Effect = default!;
}
