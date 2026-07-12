using Robust.Shared.Serialization;

namespace Content.Shared.Sprite;

public abstract partial class SharedRandomSpriteSystem : EntitySystem { }

[Serializable, NetSerializable]
public sealed class RandomSpriteColorComponentState : ComponentState
{
    public Dictionary<string, (string State, Color? Color)> Selected = default!;
}
