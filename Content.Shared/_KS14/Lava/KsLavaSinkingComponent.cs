using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Lava;

/// <summary>
///     Added to things that are sinking in lava.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class KsLavaSinkingComponent : Component
{
    /// <summary>
    ///     Game-time at which this started sinking.
    /// </summary>
    [DataField, AutoNetworkedField]
    [AutoPausedField]
    public TimeSpan StartTime = TimeSpan.MinValue;

    /// <summary>
    ///     Game-time at which this will be finished sinking.
    /// </summary>
    [DataField, AutoNetworkedField]
    [AutoPausedField]
    public TimeSpan SinkTime = TimeSpan.MinValue;

    public object? Shader = null;
}
