using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation;

/// <summary>
///     The roads a Fork in the Road offers: a qi law of one of the five elements, or the body.
/// </summary>
[Serializable, NetSerializable]
public enum CultivationPathOption : byte
{
    Metal,
    Wood,
    Water,
    Fire,
    Earth,
    Body,
    /// <summary>
    ///     The path of qi with no element yet: that comes from whichever manual they find.
    /// </summary>
    Qi,
}

[Serializable, NetSerializable]
public enum PathChoiceUiKey : byte
{
    Key,
}

/// <summary>
///     Sent from the Fork in the Road menu: walk this path.
/// </summary>
[Serializable, NetSerializable]
public sealed class PathChoiceMessage(CultivationPathOption option) : BoundUserInterfaceMessage
{
    public readonly CultivationPathOption Option = option;
}
