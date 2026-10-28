namespace Content.Server._KS14.Chemistry.Explosion;

/// <summary>
///     A container whose solution takes on the heat of the air around it, and explodes on its own once explosive contents
///         get hot enough (<see cref="Content.Shared.Chemistry.Reagent.ReagentPrototype.KsCookOffTemperature"/>): a fuel
///         tank left beside a fire.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(KsCookOffSystem))]
public sealed partial class KsCookOffComponent : Component
{
    [DataField(required: true)]
    public string Solution = default!;

    /// <summary>
    ///     Thermal conductance between the solution and the air on the container's tile, in W/K.
    /// </summary>
    [DataField]
    public float Conductance = 10f;

    [DataField]
    public TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    [DataField, AutoPausedField]
    public TimeSpan NextCheck = TimeSpan.Zero;
}
