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
        TimeSpan shootDelay,
        TimeSpan minDelay,
        TimeSpan maxDelay,
        float currentCharge,
        float maxCharge
    ) : BoundUserInterfaceState
{
    public bool IsActive = isActive;
    public double SpesosHeld = spesosHeld;
    public TimeSpan ShootDelay = shootDelay;
    public TimeSpan MinDelay = minDelay;
    public TimeSpan MaxDelay = maxDelay;
    public float CurrentCharge = currentCharge;
    public float MaxCharge = maxCharge;
}

[Serializable, NetSerializable]
public sealed class PtlToggleMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable]
public sealed class PtlSetDelayMessage(TimeSpan delay) : BoundUserInterfaceMessage
{
    public TimeSpan Delay = delay;
}

[Serializable, NetSerializable]
public sealed class PtlWithdrawMessage : BoundUserInterfaceMessage { }
