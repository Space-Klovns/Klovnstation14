using Content.Shared.Polymorph;
using Robust.Shared.GameStates;

namespace Content.Shared.Polymorph.Components;

[RegisterComponent]
[NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class PolymorphedEntityComponent : Component
{
    /// <summary>
    /// The polymorph prototype, used to track various information
    /// about the polymorph
    /// </summary>
    [DataField()]
    [AutoNetworkedField]
    public PolymorphConfiguration Configuration = new();

    /// <summary>
    /// The original entity that the player will revert back into
    /// </summary>
    [DataField()]
    [AutoNetworkedField]
    public EntityUid? Parent;

    /// <summary>
    /// Whether this polymorph has been reverted.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public bool Reverted;

    /// <summary>
    /// The amount of time that has passed since the entity was created
    /// used for tracking the duration
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public float Time;

    [DataField]
    [AutoNetworkedField]
    public EntityUid? Action;
}
