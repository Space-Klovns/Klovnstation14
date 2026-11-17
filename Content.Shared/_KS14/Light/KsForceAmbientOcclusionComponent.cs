using Robust.Shared.GameObjects;

namespace Content.Shared._KS14.Light;

/// <summary>
///     Makes an entity cast ambient occlusion even though it lies at floor depth or has no hard fixture, such as a
///         conveyor belt or a lever. Unshaded layers are still left out.
/// </summary>
[RegisterComponent]
public sealed partial class KsForceAmbientOcclusionComponent : Component;
