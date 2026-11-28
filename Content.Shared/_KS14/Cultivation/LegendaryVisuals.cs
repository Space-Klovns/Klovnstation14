using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation;

/// <summary>
///     Whether the Ruyi Jingu Bang is grown to full size or shrunk to a needle.
/// </summary>
[Serializable, NetSerializable]
public enum RuyiVisuals : byte
{
    Extended,
}

[Serializable, NetSerializable]
public enum RuyiVisualLayers : byte
{
    Base,
}
