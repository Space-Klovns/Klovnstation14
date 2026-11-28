using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation.Components;

/// <summary>
///     Which of the four category menus a cultivation, body, martial or treasure art is listed under.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CultivationArtCategoryComponent : Component
{
    [DataField(required: true)]
    public CultivationArtCategory Category;
}

[Serializable, NetSerializable]
public enum CultivationArtCategory : byte
{
    Offensive,
    Defensive,
    Mobility,
    Utility,
}
