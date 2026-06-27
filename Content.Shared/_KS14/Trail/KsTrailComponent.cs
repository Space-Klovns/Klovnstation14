using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Trail;

[Access(typeof(KsTrailSystem))]
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class KsTrailComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public SpriteSpecifier StartSprite;

    [DataField(required: true), AutoNetworkedField]
    public SpriteSpecifier Sprite;
}
