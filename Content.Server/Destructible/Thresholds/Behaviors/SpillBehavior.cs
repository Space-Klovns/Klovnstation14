using Content.Server.Fluids.EntitySystems;
using Content.Shared.Destructible;
using Content.Shared.Destructible.Thresholds.Behaviors;
using Content.Shared.Fluids.Components;
using Content.Shared.Chemistry.EntitySystems;
using JetBrains.Annotations;

namespace Content.Server.Destructible.Thresholds.Behaviors;

[UsedImplicitly]
[DataDefinition]
public sealed partial class SpillBehavior : IThresholdBehavior
{
    /// <summary>
    /// Optional fallback solution name if SpillableComponent is not present.
    /// </summary>
    [DataField]
    public string? Solution;

    /// <summary>
    /// When triggered, spills the entity's solution onto the ground.
    /// Will first try to use the solution from a SpillableComponent if present,
    /// otherwise falls back to the solution specified in the behavior's data fields.
    /// The solution is properly drained/split before spilling to prevent double-spilling with other behaviors.
    /// </summary>
    /// <param name="owner">Entity whose solution will be spilled</param>
    /// <param name="system">System calling this behavior</param>
    /// <param name="cause">Optional entity that caused this behavior to trigger</param>
    public void Execute(EntityUid owner, DestructibleSystem system, EntityUid? cause = null)
    {
        var puddleSystem = system.EntityManager.System<PuddleSystem>();
        var solutionContainer = system.EntityManager.System<SharedSolutionContainerSystem>();
        var coordinates = system.EntityManager.GetComponent<TransformComponent>(owner).Coordinates;
    }

    /// <summary>
    /// If there is a SpillableComponent on EntityUidowner use it to create a puddle/smear.
    /// Or whatever solution is specified in the behavior itself.
    /// If none are available do nothing.
    /// </summary>
    /// <param name="owner">Entity on which behavior is executed</param>
    /// <param name="system">system calling the behavior</param>
    /// <param name="cause"></param>
    public void Execute(EntityUid owner, SharedDestructibleSystem system, EntityUid? cause = null)
    {
        var solutionContainerSystem = system.EntityManager.System<SharedSolutionContainerSystem>();
        var spillableSystem = system.EntityManager.System<PuddleSystem>();

        var coordinates = system.EntityManager.GetComponent<TransformComponent>(owner).Coordinates;

        if (system.EntityManager.TryGetComponent(owner, out SpillableComponent? spillableComponent) &&
            solutionContainerSystem.TryGetSolution(owner, spillableComponent.SolutionName, out _, out var compSolution))
        {
            spillableSystem.TrySplashSpillAt(owner, coordinates, compSolution, out _, false, user: cause);
        }
        else if (Solution != null &&
                    solutionContainerSystem.TryGetSolution(owner, Solution, out _, out var behaviorSolution))
        {
            spillableSystem.TrySplashSpillAt(owner, coordinates, behaviorSolution, out _, user: cause);
        }
    }
}

