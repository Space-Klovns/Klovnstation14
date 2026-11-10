using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Voice;

/// <summary>
///     Shows a "talking" icon above an entity while its player is transmitting voice. Added by the server the
///         first time the entity talks; the talking state itself travels as appearance data
///         (<see cref="KsVoiceVisuals.Talking"/>).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsVoiceIndicatorComponent : Component
{
    [DataField]
    public SpriteSpecifier.Rsi Sprite = new(new ResPath("/Textures/_KS14/Effects/voice_indicator.rsi"), "talking");

    /// <summary>
    ///     Shader for the icon. None by default, so the icon is lit like the mob it sits on: an unshaded icon would
    ///         glow in the dark and give away anyone talking in an unlit room.
    /// </summary>
    [DataField]
    public string? Shader;

    [DataField]
    public Vector2 Offset = new(0f, 0f);
}

[Serializable, NetSerializable]
public enum KsVoiceVisuals : byte
{
    Talking,
}

[Serializable, NetSerializable]
public enum KsVoiceIndicatorLayers : byte
{
    Base,
}
