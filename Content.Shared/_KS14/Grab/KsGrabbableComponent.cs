using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._KS14.Grab;

/// <summary>
///     Someone a <see cref="KsGrabberComponent"/> can grab harder than a plain pull, and who can resist the grip.
///     Anything pullable without this is only ever pulled.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(KsGrabSystem))]
public sealed partial class KsGrabbableComponent : Component
{
    /// <summary>
    ///     Multiplies every stage's escape chance: above 1 is slippery, below 1 easy to hold.
    /// </summary>
    [DataField]
    public float EscapeChanceModifier = 1f;

    /// <summary>
    ///     tgstation's <c>CLICK_CD_RESIST</c>: time between two resist attempts.
    /// </summary>
    [DataField]
    public TimeSpan ResistCooldown = TimeSpan.FromSeconds(2);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextResist;
}
