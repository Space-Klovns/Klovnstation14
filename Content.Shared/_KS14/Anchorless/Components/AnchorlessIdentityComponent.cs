using System.Linq;
using Content.Shared.Actions;
using Content.Shared.Cloning;
using Content.Shared.DoAfter;
using Content.Shared.Roles;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Anchorless.Components;

/// <summary>
/// Stores the identities remembered by an Anchorless player.
/// </summary>
[DataDefinition]
public sealed partial class AnchorlessIdentityData
{
    [DataField]
    public EntityUid? StoredIdentity;

    [DataField]
    public EntityUid? OriginalEntity;

    [DataField]
    public string OriginalName = "Unnamed";

    [DataField]
    public bool Starting = false;
}

/// <summary>Network representation of an Anchorless identity.</summary>
[Serializable, NetSerializable]
public sealed class AnchorlessIdentityComponentState(
    List<AnchorlessNetworkedIdentityData> learnedIdentities,
    NetEntity? currentIdentity,
    ProtoId<CloningSettingsPrototype> identityCloningSettings,
    bool horrorForm,
    ResPath horrorSprite,
    string horrorSpriteState) : ComponentState
{
    public List<AnchorlessNetworkedIdentityData> LearnedIdentities = learnedIdentities;
    public NetEntity? CurrentIdentity = currentIdentity;
    public ProtoId<CloningSettingsPrototype> IdentityCloningSettings = identityCloningSettings;
    public bool HorrorForm = horrorForm;
    public ResPath HorrorSprite = horrorSprite;
    public string HorrorSpriteState = horrorSpriteState;
}

[Serializable, NetSerializable]
public sealed class AnchorlessNetworkedIdentityData
{
    public NetEntity? StoredIdentity;
    public NetEntity? OriginalEntity;
    public string OriginalName = "Unnamed";
    public bool Starting;
}

public sealed partial class AnchorlessTransformActionEvent : InstantActionEvent;
public sealed partial class AnchorlessHorrorActionEvent : InstantActionEvent;
public sealed partial class AnchorlessConvertActionEvent : EntityTargetActionEvent;

/// <summary>Completes an Anchorless conversion after its visible ritual has finished.</summary>
[Serializable, NetSerializable]
public sealed partial class AnchorlessConvertDoAfterEvent : SimpleDoAfterEvent;

public static class AnchorlessIdentityHelper
{
    public static List<AnchorlessIdentityData> MergeIdentityData(IEnumerable<AnchorlessIdentityData> first, IEnumerable<AnchorlessIdentityData> second)
    {
        var merged = new List<AnchorlessIdentityData>();

        foreach (var item in first.Concat(second))
        {
            if (merged.Any(existing =>
                    item.OriginalEntity != null && existing.OriginalEntity == item.OriginalEntity ||
                    item.OriginalEntity == null && existing.OriginalEntity == null && existing.OriginalName == item.OriginalName))
                continue;

            merged.Add(new AnchorlessIdentityData
            {
                StoredIdentity = item.StoredIdentity,
                OriginalEntity = item.OriginalEntity,
                OriginalName = item.OriginalName,
                Starting = item.Starting,
            });
        }

        return merged;
    }
}
