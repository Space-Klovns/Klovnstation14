using Content.Server._KS14.Objectives.Components;
using Content.Server._KS14.Anchorless.Systems;
using Content.Shared._KS14.Anchorless.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Objectives.Components;

namespace Content.Server._KS14.Objectives.Systems;

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
        ent.Comp.ConvertedMinds.Add(args.MindId);
    }

    private void OnConverted(ref AnchorlessConvertedEvent args)
    {
        if (!TryComp<MindContainerComponent>(args.Converted, out var mind) || mind.Mind == null)
            return;

        var objectives = EntityQueryEnumerator<AnchorlessConvertCrewConditionComponent>();
        while (objectives.MoveNext(out _, out var objective))
            objective.ConvertedMinds.Add(mind.Mind.Value);
    }

    private void OnGetProgress(Entity<AnchorlessConvertCrewConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        var (crewCount, anchorlessCount) = GetPopulationCounts();
        if (crewCount == 0)
        {
            args.Progress = 1f;
            return;
        }

        var requiredAnchorlessCount = (int) Math.Ceiling(crewCount * ent.Comp.RequiredFraction);
        args.Progress = Math.Min(1f, (float) anchorlessCount / requiredAnchorlessCount);
    }

    private (int Crew, int Anchorless) GetPopulationCounts()
    {
        var crew = 0;
        var anchorless = 0;
        var query = EntityQueryEnumerator<HumanoidProfileComponent, MindContainerComponent>();
        while (query.MoveNext(out _, out _, out var mind))
        {
            if (!mind.HasMind)
                continue;

            crew++;
            if (mind.Mind is not { } mindId || !TryComp<MindComponent>(mindId, out var mindComponent))
                continue;

            foreach (var role in mindComponent.MindRoleContainer.ContainedEntities)
            {
                if (!HasComp<AnchorlessRoleComponent>(role))
                    continue;

                anchorless++;
                break;
            }
        }

        return (crew, anchorless);
    }}
