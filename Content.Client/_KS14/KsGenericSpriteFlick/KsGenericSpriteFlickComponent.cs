using Robust.Client.Animations;

namespace Content.Client._KS14.GenericSpriteFlick;

[RegisterComponent]
public sealed partial class KsGenericSpriteFlickComponent : Component
{
    /// <summary>
    ///     Flick animations cached by the state and layer key.
    /// </summary>
    public Dictionary<(string, object), Animation> CachedAnimations = [];
}
