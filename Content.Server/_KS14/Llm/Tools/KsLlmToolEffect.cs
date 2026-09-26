using Content.Shared.Paper;

namespace Content.Server._KS14.Llm.Tools;

/// <summary>
///     What a <see cref="Prototypes.KsLlmToolPrototype"/> actually does. Runs on the main thread only, with
///         arguments already validated against the tool's parameters - but everything the arguments *refer to*
///         (entities, names, levels) is the effect's to check, since the model can name anything.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class KsLlmToolEffect
{
    /// <summary>
    ///     Performs the action. The returned message is shown to the model, so say what happened, or why not,
    ///         in a way it can act on.
    /// </summary>
    public abstract KsLlmToolOutcome Execute(in KsLlmToolContext context);
}

/// <summary>
///     Everything a tool effect gets to know about the call and the fax that prompted it.
/// </summary>
public readonly struct KsLlmToolContext
{
    public required IEntityManager EntityManager { get; init; }

    public required ILocalizationManager Localization { get; init; }

    public required KsLlmToolArguments Arguments { get; init; }

    /// <summary>
    ///     The fax machine the persona answers from (Central Command's).
    /// </summary>
    public required EntityUid RecipientFaxUid { get; init; }

    /// <summary>
    ///     The fax machine that sent the message, if it could be found.
    /// </summary>
    public required EntityUid? SenderFaxUid { get; init; }

    /// <summary>
    ///     The station the sender fax belongs to, if any.
    /// </summary>
    public required EntityUid? StationUid { get; init; }

    /// <summary>
    ///     The stamps on the paper that started this turn. Authorisation checks go through these, never
    ///         through anything the model says.
    /// </summary>
    public required IReadOnlyList<StampDisplayInfo> Stamps { get; init; }
}
