using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Fluidmos.Reactions;

/// <summary>
///     Appearance data of a catalytic reactor.
/// </summary>
[Serializable, NetSerializable]
public enum KsCatalyticReactorVisuals : byte
{
    Reacting,
}
