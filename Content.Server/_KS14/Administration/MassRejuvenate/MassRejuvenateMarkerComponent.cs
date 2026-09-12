using Robust.Shared.GameStates;

namespace Content.Server._KS14.Administration.MassRejuvenate;

[RegisterComponent]
[Access(typeof(MassRejuvenateSystem))]
public sealed partial class MassRejuvenateMarkerComponent : Component
{
    [DataField]
    public float Radius = 2f;

    [DataField]
    public bool PlayerControlledOnly = true;
}