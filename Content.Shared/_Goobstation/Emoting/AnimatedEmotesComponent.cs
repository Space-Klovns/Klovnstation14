namespace Content.Shared._Goobstation.Emoting;

/// <summary>
///     Marks entities that can use animated emotes.
/// </summary>
[RegisterComponent]
public sealed partial class AnimatedEmotesComponent : Component;

[DataDefinition] public sealed partial class AnimationFlipEmoteEvent;
[DataDefinition] public sealed partial class AnimationSpinEmoteEvent;
[DataDefinition] public sealed partial class AnimationJumpEmoteEvent;
