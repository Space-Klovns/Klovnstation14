using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Chemistry.PressureGatedInjector;

/// <summary>
///     An injector made for low pressure, as tg's survival medipens are: where the air is thin it works as any other,
///         but anywhere thicker its gauge has to be bled by hand first, and only part of the dose gets in.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsPressureGatedInjectorComponent : Component
{
    /// <summary>
    ///     The most pressure, in kPa, at which the injector works freely. tg's <c>LAVALAND_EQUIPMENT_EFFECT_PRESSURE</c>.
    /// </summary>
    [DataField]
    public float MaxPressure = 50f;

    /// <summary>
    ///     How much longer an injection takes above <see cref="MaxPressure"/>.
    /// </summary>
    [DataField]
    public TimeSpan HighPressureDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How much of the dose gets in above <see cref="MaxPressure"/>. The rest is lost.
    /// </summary>
    [DataField]
    public float HighPressureAmount = 0.5f;

    /// <summary>
    ///     Whether the injector is where the pressure is low enough. Kept by the server, which alone knows the air, so the
    ///         client can predict the injection.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool LowPressure;
}
