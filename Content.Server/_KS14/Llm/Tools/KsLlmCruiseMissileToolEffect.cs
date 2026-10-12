using Content.Server.Station.Systems;
using Content.Shared.Mobs.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Llm.Tools;

/// <summary>
///     Drops an orbital cruise missile on a named player who is aboard a station.
/// </summary>
/// <remarks>
///     Every safeguard is enforced in code, not asked of the model: the authorising stamps and the per-round
///         limit live on the tool prototype and are checked before this runs. A persuasive fax can talk the model
///         into calling this; it cannot talk this into firing.
/// </remarks>
public sealed partial class KsLlmCruiseMissileToolEffect : KsLlmToolEffect
{
    [DataField]
    public string TargetArgument = "target_name";

    [DataField]
    public string ArmedArgument = "armed";

    [DataField(required: true)]
    public EntProtoId InertPrototype;

    [DataField(required: true)]
    public EntProtoId ArmedPrototype;

    public override KsLlmToolOutcome Execute(in KsLlmToolContext context)
    {
        var targetName = context.Arguments.GetString(TargetArgument) ?? string.Empty;
        var armed = context.Arguments.GetBool(ArmedArgument) ?? false;

        var entityManager = context.EntityManager;
        var stationSystem = entityManager.System<StationSystem>();

        // Only players with a body, so a ghost drifting over the station is never a match.
        EntityUid? targetUid = null;
        var matches = 0;
        var actorQuery = entityManager.EntityQueryEnumerator<ActorComponent, MobStateComponent, MetaDataComponent>();
        while (actorQuery.MoveNext(out var actorUid, out _, out _, out var metaDataComponent))
        {
            if (!string.Equals(metaDataComponent.EntityName.Trim(), targetName, StringComparison.OrdinalIgnoreCase))
                continue;

            matches++;
            targetUid = actorUid;
        }

        if (matches == 0 || targetUid == null)
            return KsLlmToolOutcome.Error($"no crew member named '{targetName}' could be found. Use their exact full name.");

        if (matches > 1)
            return KsLlmToolOutcome.Error($"{matches} people are named '{targetName}'; the target is ambiguous.");

        if (stationSystem.GetOwningStation(targetUid.Value) == null)
            return KsLlmToolOutcome.Error($"'{targetName}' is not aboard a station.");

        // Map coordinates rather than the target's own: a target inside a locker would otherwise parent the
        // missile to the locker.
        var coordinates = entityManager.System<SharedTransformSystem>().GetMapCoordinates(targetUid.Value);
        entityManager.SpawnEntity(armed ? ArmedPrototype : InertPrototype, coordinates);

        return KsLlmToolOutcome.Ok(armed
            ? $"An armed cruise missile has been launched at {targetName}."
            : $"An inert cruise missile has been launched at {targetName}.");
    }
}
