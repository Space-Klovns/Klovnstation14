using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Damage.Components;

[NetworkedComponent, RegisterComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause] // KS14: networked (NextSecond paused too), so the client's contact damage ticks with the server's
public sealed partial class DamagedByContactComponent : Component
{
    [DataField("nextSecond", customTypeSerializer: typeof(TimeOffsetSerializer)), ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField, AutoPausedField] // KS14
    public TimeSpan NextSecond = TimeSpan.Zero;

    [ViewVariables]
    [AutoNetworkedField] // KS14
    public DamageSpecifier? Damage;
}
