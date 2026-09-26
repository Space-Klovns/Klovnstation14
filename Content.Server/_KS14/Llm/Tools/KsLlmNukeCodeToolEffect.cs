using Content.Server.Nuke;

namespace Content.Server._KS14.Llm.Tools;

/// <summary>
///     Tells the model the authentication code of the sending station's own nuclear fission explosive, so it
///         can pass it on in its reply. The code arms the bomb together with the nuclear authentication disk.
/// </summary>
/// <remarks>
///     Who may ask is the tool prototype's <c>requiredStamps</c>, checked before this runs.
/// </remarks>
public sealed partial class KsLlmNukeCodeToolEffect : KsLlmToolEffect
{
    public override KsLlmToolOutcome Execute(in KsLlmToolContext context)
    {
        if (context.StationUid is not { } stationUid)
            return KsLlmToolOutcome.Error("the sending fax does not belong to any station.");

        var entityManager = context.EntityManager;

        // Only the station's own bomb: a code for some other station's, or a nuclear operative's, is nobody's
        // business here.
        var nukeQuery = entityManager.EntityQueryEnumerator<NukeComponent, MetaDataComponent>();
        while (nukeQuery.MoveNext(out _, out var nukeComponent, out var metaDataComponent))
        {
            if (nukeComponent.OriginStation != stationUid || string.IsNullOrEmpty(nukeComponent.Code))
                continue;

            return KsLlmToolOutcome.Ok($"The authentication code for the station's {metaDataComponent.EntityName} is {nukeComponent.Code}. "
                                       + "It arms the device only together with the nuclear authentication disk.");
        }

        return KsLlmToolOutcome.Error("no nuclear fission explosive is registered to the sending station.");
    }
}
