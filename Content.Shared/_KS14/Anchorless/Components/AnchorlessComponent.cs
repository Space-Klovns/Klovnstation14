using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Content.Shared.Cloning;

namespace Content.Shared._KS14.Anchorless.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class AnchorlessComponent : Component
{
    [DataField]
    public TimeSpan GunFlashDuration = TimeSpan.FromSeconds(1);

    [DataField]
    public float GunFlashSlowdown = 0.7f;

    [DataField]
    public List<AnchorlessIdentityData> LearnedIdentities = new();

    [DataField]
    public EntityUid? CurrentIdentity;

    [DataField]
    public ProtoId<CloningSettingsPrototype> IdentityCloningSettings = "ChangelingCloningSettings";

    [DataField]
    public bool HorrorForm;

    /// <summary>
    /// The sprite displayed while the Anchorless has revealed its true form.
    /// Kept here so individual Anchorless prototypes can define their own horror form.
    /// </summary>
    [DataField]
    public ResPath HorrorSprite = new("/Textures/_KS14/Mobs/Anchorless/horror.rsi");

    [DataField]
    public string HorrorSpriteState = "horror";

    [DataField]
    public EntityUid? HorrorArmbladeAction;

    public override bool SendOnlyToOwner => true;
}

