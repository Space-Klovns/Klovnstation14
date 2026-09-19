using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Anchorless.Components;

/// <summary>
/// Public visual state for an Anchorless' horror form.
/// This is separate from <see cref="KsAnchorlessAntagComponent"/> so that its
/// owner-only identity data is not replicated to other players.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class AnchorlessHorrorVisualComponent : Component
{
    [AutoNetworkedField]
    public bool HorrorForm;

    [AutoNetworkedField]
    public ResPath HorrorSprite = new("/Textures/_KS14/Mobs/Anchorless/horror.rsi");

    [AutoNetworkedField]
    public string HorrorSpriteState = "horror";

    [AutoNetworkedField]
    public Vector2 HorrorScale = new(0.5f, 0.5f);
}

/// <summary>Raised after the authoritative horror form state changes.</summary>
public sealed partial class AnchorlessHorrorFormChangedEvent : EntityEventArgs;
