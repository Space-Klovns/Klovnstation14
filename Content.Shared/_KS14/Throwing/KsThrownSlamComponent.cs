using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Throwing;

/// <summary>
///     Someone sent flying, still in flight. Whatever they slam into first knocks them down, and knocks over whoever
///         they hit, as a shove would. Removed by that first slam, or when they land.
///     Added with <see cref="KsThrownSlamSystem.MarkThrown"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsThrownSlamSystem))]
public sealed partial class KsThrownSlamComponent : Component
{
    /// <summary>
    ///     What the slam does.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<KsThrownSlamPrototype> Slam;

    /// <summary>
    ///     Who sent them flying, if anyone did: null for a blast or a machine. They are never slammed into.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Source;
}
