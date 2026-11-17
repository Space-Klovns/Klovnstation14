using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Maths;

namespace Content.Shared._KS14.Chemistry.SolutionHeatExchange;

/// <summary>
///     Lets two solutions trade heat until they share a temperature, conserving total thermal energy.
/// </summary>
public sealed partial class KsSolutionHeatExchangeSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;

    /// <summary>
    ///     Brings two solutions to a common temperature, T = (C1*T1 + C2*T2) / (C1 + C2), where C is the heat
    ///         capacity (J/K). Total thermal energy is unchanged.
    /// </summary>
    /// <returns>
    ///     True if the temperatures were changed. False if the solutions have no combined heat capacity
    ///         or are already at (nearly) the same temperature.
    /// </returns>
    public bool TryEqualizeTemperature(Entity<SolutionComponent> first, Entity<SolutionComponent> second)
    {
        if (first.Owner == second.Owner)
            return false;

        var firstSolution = first.Comp.Solution;
        var secondSolution = second.Comp.Solution;

        var firstHeatCapacity = firstSolution.GetHeatCapacity(ProtoMan);
        var secondHeatCapacity = secondSolution.GetHeatCapacity(ProtoMan);
        var totalHeatCapacity = firstHeatCapacity + secondHeatCapacity;

        if (totalHeatCapacity <= 0f)
            return false;

        var firstTemperature = firstSolution.Temperature;
        var secondTemperature = secondSolution.Temperature;

        if (MathHelper.CloseTo(firstTemperature, secondTemperature))
            return false;

        var equilibriumTemperature = ((firstHeatCapacity * firstTemperature) + (secondHeatCapacity * secondTemperature)) / totalHeatCapacity;

        // Both temperatures first, so no reaction runs while only one side has equalised.
        firstSolution.Temperature = equilibriumTemperature;
        secondSolution.Temperature = equilibriumTemperature;

        _solutionContainerSystem.UpdateChemicals(first);
        _solutionContainerSystem.UpdateChemicals(second);
        return true;
    }
}
