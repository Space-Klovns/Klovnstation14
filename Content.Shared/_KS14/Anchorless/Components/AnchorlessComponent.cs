using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Anchorless.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class AnchorlessComponent : Component
{
    [DataField]
    public TimeSpan GunFlashDuration = TimeSpan.FromSeconds(1);

    [DataField]
    public float GunFlashSlowdown = 0.7f;
}

