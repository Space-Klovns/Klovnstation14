using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.GenericSpriteFlick;

[RegisterComponent, NetworkedComponent]
public sealed partial class KsGenericSpriteFlickFinishStateComponent : Component
{
    /// <summary>
    ///     End states cached by
    ///         key: anim id, layer key;
    ///         value: end finish state;
    /// </summary>
    public Dictionary<(string, string), string> FinishStates = [];
}

// insane name
[Serializable, NetSerializable]
public sealed class KsGenericSpriteFlickFinishStateComponentState(Dictionary<(string, string), string> finishStates) : ComponentState
{
    public Dictionary<(string, string), string> FinishStates = finishStates;
}
