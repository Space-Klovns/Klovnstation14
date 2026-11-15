using Content.Shared.Mobs;
using Robust.Client.Graphics;

namespace Content.Client._KS14.FieldCommander;

/// <summary>
///     What the client keeps for a defender it flashes as they go down: the state they were last seen in, to tell a
///         change from a first sight, and the flash in progress. Client-only.
/// </summary>
[RegisterComponent]
public sealed partial class KsFieldCommanderDownFlashVisualsComponent : Component
{
    [ViewVariables]
    public MobState LastState;

    /// <summary>
    ///     The flash in progress, or null while there is none.
    /// </summary>
    [ViewVariables]
    public ShaderInstance? Shader;

    /// <summary>
    ///     When the flash began, on the client's clock.
    /// </summary>
    [ViewVariables]
    public TimeSpan StartTime;

    [ViewVariables]
    public TimeSpan Duration;

    /// <summary>
    ///     How far through the flash it was as of the last frame, 0 to 1.
    /// </summary>
    [ViewVariables]
    public float Progress;

    [ViewVariables]
    public Color Color;

    [ViewVariables]
    public float Seed;
}
