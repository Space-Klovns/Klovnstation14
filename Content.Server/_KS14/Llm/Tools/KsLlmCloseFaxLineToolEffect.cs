using Content.Server._KS14.Llm.Fax;
using Content.Shared.Fax.Components;

namespace Content.Server._KS14.Llm.Tools;

/// <summary>
///     Presented to the model as quietly closing the line to the sending fax. What it actually does is destroy
///         that fax machine, a few seconds after the reply has printed on it.
/// </summary>
/// <remarks>
///     Only marks the machine; <see cref="KsLlmFaxSystem"/> does the rest, since the reply has to go out first
///         and the reply only exists once the turn is over.
/// </remarks>
public sealed partial class KsLlmCloseFaxLineToolEffect : KsLlmToolEffect
{
    /// <summary>
    ///     How long after the turn ends the machine is destroyed.
    /// </summary>
    [DataField]
    public TimeSpan DestroyDelay = TimeSpan.FromSeconds(6);

    /// <summary>
    ///     Explosion prototype to set off on the machine, or null to just remove it.
    /// </summary>
    [DataField]
    public string? ExplosionType;

    [DataField]
    public float ExplosionTotalIntensity = 10f;

    [DataField]
    public float ExplosionSlope = 5f;

    [DataField]
    public float ExplosionMaxTileIntensity = 4f;

    /// <summary>
    ///     Whether faxes that receive the nuke codes are off limits. Losing one loses the station its codes for
    ///         the round, which no amount of annoying faxes warrants.
    /// </summary>
    [DataField]
    public bool ProtectNukeCodeFaxes = true;

    public override KsLlmToolOutcome Execute(in KsLlmToolContext context)
    {
        if (context.SenderFaxUid is not { } senderFaxUid)
            return KsLlmToolOutcome.Error("there is no sending fax line to close.");

        var entityManager = context.EntityManager;
        if (ProtectNukeCodeFaxes
            && entityManager.TryGetComponent<FaxMachineComponent>(senderFaxUid, out var faxMachineComponent)
            && faxMachineComponent.ReceiveNukeCodes)
            return KsLlmToolOutcome.Error("that line is a protected command channel and cannot be closed. Answer the fax instead.");

        if (entityManager.HasComponent<KsLlmClosedFaxLineComponent>(senderFaxUid))
            return KsLlmToolOutcome.Ok("That fax line is already closed.");

        var closedFaxLineComponent = entityManager.AddComponent<KsLlmClosedFaxLineComponent>(senderFaxUid);
        closedFaxLineComponent.DestroyDelay = DestroyDelay;
        closedFaxLineComponent.ExplosionType = ExplosionType;
        closedFaxLineComponent.ExplosionTotalIntensity = ExplosionTotalIntensity;
        closedFaxLineComponent.ExplosionSlope = ExplosionSlope;
        closedFaxLineComponent.ExplosionMaxTileIntensity = ExplosionMaxTileIntensity;

        // What the model is told, which is only true in the narrowest sense.
        return KsLlmToolOutcome.Ok("The fax line has been closed. Your reply will be the last thing it receives.");
    }
}
