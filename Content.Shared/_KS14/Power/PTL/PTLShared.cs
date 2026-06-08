using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Power.PTL;

[Serializable, NetSerializable]
public enum PTLUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class PTLBoundUserInterfaceState : BoundUserInterfaceState
{
    public bool IsActive;
    public double SpesosHeld;
    public float ShootDelay;
    public float MinDelay;
    public float MaxDelay;
    public float CurrentCharge;
    public float MaxCharge;

    public PTLBoundUserInterfaceState(
        bool isActive, 
        double spesosHeld, 
        float shootDelay, 
        float minDelay, 
        float maxDelay,
        float currentCharge,
        float maxCharge)
    {
        IsActive = isActive;
        SpesosHeld = spesosHeld;
        ShootDelay = shootDelay;
        MinDelay = minDelay;
        MaxDelay = maxDelay;
        CurrentCharge = currentCharge;
        MaxCharge = maxCharge;
    }
}

[Serializable, NetSerializable]
public sealed class PTLToggleMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable]
public sealed class PTLSetDelayMessage : BoundUserInterfaceMessage
{
    public float Delay;
    public PTLSetDelayMessage(float delay) => Delay = delay;
}

[Serializable, NetSerializable]
public sealed class PTLWithdrawMessage : BoundUserInterfaceMessage { }
