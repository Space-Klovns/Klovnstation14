using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Dissolve;

/// <summary>
///     Marks an entity that is dissolving away. The client dissolves its sprite for <see cref="Duration"/>; the server
///         deletes the entity a moment after it ends (<c>KsDissolveSystem.Dissolve</c>). Added at runtime, never in a
///         prototype. Taken off again, and the entity kept, if it is picked up or put in something first.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class KsDissolveComponent : Component
{
    /// <summary>
    ///     When the dissolve began.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan StartTime;

    /// <summary>
    ///     How long the sprite takes to dissolve.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan Duration = TimeSpan.FromSeconds(1.5);

    /// <summary>
    ///     Server only: whether the dissolve put the despawn timer on the entity itself, and so takes it off again if
    ///         the dissolve is called off. One that was already counting down keeps its own.
    /// </summary>
    [ViewVariables]
    public bool OwnsDespawn;
}
