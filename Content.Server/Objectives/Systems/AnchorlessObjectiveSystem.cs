using Content.Server.Objectives.Components;
using Content.Shared._KS14.Anchorless.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mind.Components;
using Content.Shared.Objectives.Components;

namespace Content.Server.Objectives.Systems;

/// <summary>
/// Calculates the Anchorless conversion objective from the currently active crew.
/// A crew member is a humanoid body with a player mind; this deliberately excludes
/// ghosts, NPC humanoids, and empty bodies while retaining dead crew as valid targets.
/// </summary>
public sealed partial class AnchorlessObjectiveSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<AnchorlessConvertCrewConditionComponent, ObjectiveAssignedEvent>(OnAssigned);
        SubscribeLocalEvent<AnchorlessConvertCrewConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
        SubscribeLocalEvent<AnchorlessConvertedEvent>(OnConverted);
    }

    private void OnAssigned(Entity<AnchorlessConvertCrewConditionComponent> ent, ref ObjectiveAssignedEvent args)
    {
        ent.Comp.RequiredConversions = (int) Math.Ceiling(GetCrewCount() * ent.Comp.RequiredFraction);
    }

    private void OnConverted(AnchorlessConvertedEvent args)
    {
        if (!TryComp<MindContainerComponent>(args.Converted, out var mind) || mind.Mind == null)
            return;

        var objectives = EntityQueryEnumerator<AnchorlessConvertCrewConditionComponent>();
        while (objectives.MoveNext(out _, out var objective))
            objective.ConvertedMinds.Add(mind.Mind.Value);
    }

    private void OnGetProgress(Entity<AnchorlessConvertCrewConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        args.Progress = ent.Comp.RequiredConversions == 0
            ? 1f
            : Math.Min(1f, (float) ent.Comp.ConvertedMinds.Count / ent.Comp.RequiredConversions);
    }

    private int GetCrewCount()
    {
        var crew = 0;
        var query = EntityQueryEnumerator<HumanoidProfileComponent, MindContainerComponent>();
        while (query.MoveNext(out _, out _, out var mind))
        {
            if (!mind.HasMind)
                continue;

            crew++;
        }
        return crew;
    }
}
