using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation;

/// <summary>
///     The powers of the Mandate of Heaven, all reached from the one Imperial Court button.
/// </summary>
[Serializable, NetSerializable]
public enum MandatePower : byte
{
    Decree,
    Kneel,
    Treasury,
    Pardon,
    Jinyiwei,
    Fealty,
    Ennoble,
    Punishment,
}

public sealed partial class ImperialCourtActionEvent : InstantActionEvent;

/// <summary>
///     What the Imperial Court menu shows: the ruler's powers, when each is ready again, and who is near enough to
///         name.
/// </summary>
[Serializable, NetSerializable]
public sealed class ImperialCourtState(List<MandatePower> powers, Dictionary<MandatePower, TimeSpan> readyAt, List<NetEntity> people)
    : BoundUserInterfaceState
{
    public readonly List<MandatePower> Powers = powers;
    public readonly Dictionary<MandatePower, TimeSpan> ReadyAt = readyAt;
    public readonly List<NetEntity> People = people;
}

[Serializable, NetSerializable]
public sealed class ImperialCourtPowerMessage(MandatePower power, NetEntity? target) : BoundUserInterfaceMessage
{
    public readonly MandatePower Power = power;
    public readonly NetEntity? Target = target;
}

[Serializable, NetSerializable]
public enum ImperialCourtUiKey : byte
{
    Key,
}

public static class MandatePowers
{
    /// <summary>
    ///     Whether a power is used on someone (and so needs them named).
    /// </summary>
    public static bool Targeted(MandatePower power)
    {
        return power is MandatePower.Pardon or MandatePower.Jinyiwei or MandatePower.Fealty or MandatePower.Ennoble or MandatePower.Punishment;
    }
}
