namespace Content.Server._KS14.Light;

/// <summary>
///     A handheld light that is switched on when it spawns, such as a floodlight put up by an RCD.
///         <see cref="Content.Shared.Light.Components.HandheldLightComponent.Activated"/> alone leaves it dark, as nothing
///         lights it at spawn.
/// </summary>
[RegisterComponent]
public sealed partial class KsHandheldLightStartsOnComponent : Component;
