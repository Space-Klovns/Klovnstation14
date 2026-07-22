using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.Overlays;

/// <summary>
/// Enables the night-vision fullscreen overlay for the entity it is attached to or the wearer.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PhosphorNightVisionComponent : Component
{
    /// <summary>
    /// Whether the overlay should be visible.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled; // Mono - false so you dont get flashbanged from helmets lol

    /// <summary>
    /// Whether this night vision is prioritized.
    /// Causes it to overwrite all other sources of night vision, even if their noise is smaller.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Prioritized = true;

    /// <summary>
    /// Whether wearing this entity should grant night vision to the entity wearing it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool RelayOverlay;

    /// <summary>
    /// The action proto that toggles the night vision.
    /// </summary>
    /// <remarks>
    /// if null, no action is added.
    /// if <see cref="RelayOverlay"/> is true. it adds the action to the entity wearing this.
    /// otherwise it adds the action to itself
    /// </remarks>
    [DataField]
    public EntProtoId? Action;

    /// <summary>
    /// Reference to the action entity
    /// </summary>
    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>
    /// Overall color modulation applied on top of the night-vision screen shader.
    /// Does not control lighting coloring, just serves as an effect on the screen.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color OverlayColor = Color.Transparent; // Transparent by default, no overlay.

    /// <summary>
    /// Color modification added on top of lighting during rendering.
    /// This is the part responsible for making things bright.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color LightingColor = new(1f, 1f, 1f, 0.15f);

    /// <summary>
    /// The color of the night vision phosphor that will be displayed as a monochromatic color to the user.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color PhosphorColor = new(0f, 1f, 0f, 1f);

    /// <summary>
    /// The amount of light multiplication the night vision system should apply.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Amplification = 32f;

    /// <summary>
    /// KS14 - do we draw the phosphor effect?
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool PhosphorEffect = true;

    /// <summary>
    /// KS14 - does this provide clean full screen vision or just a cone?
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsCone = false;

    /// <summary>
    /// KS14 - the width of the cone in degrees
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ConeAngle = 90f;

    /// <summary>
    /// KS14 - softness of the edges in degrees - dictates transition smoothness (so we dont get a sharp split between bright and dark)
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ConeFeather = 5f;

    /// <summary>
    /// KS14 - how far does the cone reach out from the player? measured in screens
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ConeDistance = 0.5f;

    /// <summary>
    /// KS14 - softness of the transition at the cone's outer radius - measured in screens also
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ConeDistanceFeather = 0.05f;
}
public sealed partial class TogglePhosphorNightVisionEvent : InstantActionEvent;
