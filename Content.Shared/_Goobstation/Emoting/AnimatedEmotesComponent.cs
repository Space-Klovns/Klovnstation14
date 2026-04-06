namespace Content.Shared._Goobstation.Emoting;

/// <summary>
///     Marks entities that can use animated emotes.
/// </summary>
[RegisterComponent]
public sealed partial class AnimatedEmotesComponent : Component;

[DataDefinition]
public sealed partial class AnimationEmoteEvent
{
    [DataField] public string AnimationKey = "emoteAnimKeyId";
}
