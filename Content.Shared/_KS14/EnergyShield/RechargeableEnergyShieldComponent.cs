using Robust.Shared.GameStates;

namespace Content.Shared._KS14.EnergyShield;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedRechargeableEnergyShieldSystem))]
public sealed partial class RechargeableEnergyShieldComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public bool ChargeDepleted;

    [ViewVariables]
    public bool ShutdownHandled;
}
