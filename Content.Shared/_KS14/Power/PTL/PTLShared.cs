using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Power.PTL;

[Serializable, NetSerializable]
public enum PTLUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class PtlBoundUserInterfaceState(
        bool isActive,
        double spesosHeld,
        float shootDelay,
        float minDelay,
        float maxDelay,
        float currentCharge,
        float maxCharge
    ) : BoundUserInterfaceState
{
    public bool IsActive = isActive;
    public double SpesosHeld = spesosHeld;
    public float ShootDelay = shootDelay;
    public float MinDelay = minDelay;
    public float MaxDelay = maxDelay;
    public float CurrentCharge = currentCharge;
    public float MaxCharge = maxCharge;
}

[Serializable, NetSerializable]
public sealed class PtlToggleMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable]
public sealed class PtlSetDelayMessage(float delay) : BoundUserInterfaceMessage
{
    public float Delay = delay;
}

[Serializable, NetSerializable]
public sealed class PtlWithdrawMessage : BoundUserInterfaceMessage { }
