using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Tank;

/// <summary>
///     Because DamageVisualsComponent doesn't support non-enums as a layer key
/// </summary>
[Serializable, NetSerializable]
public enum TankVisualLayers : byte
{
    Base,
    Armor
}
