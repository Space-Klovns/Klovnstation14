using Robust.Shared.Serialization;

namespace Content.Shared._Goobstation.Emoting;

/// <summary>
///     Marks entities that can use animated emotes.
/// </summary>
[RegisterComponent]
public sealed partial class AnimatedEmotesComponent : Component;

[Serializable, NetSerializable, DataDefinition] public sealed partial class AnimationFlipEmoteEvent : EntityEventArgs { }
[Serializable, NetSerializable, DataDefinition] public sealed partial class AnimationSpinEmoteEvent : EntityEventArgs { }
[Serializable, NetSerializable, DataDefinition] public sealed partial class AnimationJumpEmoteEvent : EntityEventArgs { }
