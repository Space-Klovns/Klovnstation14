using System.Linq;
using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Anchorless.Components;

/// <summary>
/// Stores the identities remembered by an Anchorless player.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AnchorlessIdentityComponent : Component
{
    [DataField]
    public List<AnchorlessIdentityData> LearnedIdentities = new();

    [DataField]
    public int CurrentIdentityIndex = 0;
}

[Serializable, NetSerializable]
[DataDefinition]
public sealed partial class AnchorlessIdentityData
{
    [DataField]
    public NetEntity? StoredIdentity;

    [DataField]
    public NetEntity? OriginalEntity;

    [DataField]
    public string OriginalName = "Unnamed";

    [DataField]
    public bool Starting = false;
}

public sealed partial class AnchorlessTransformActionEvent : InstantActionEvent;

public static class AnchorlessIdentityHelper
{
    public static List<AnchorlessIdentityData> MergeIdentityData(IEnumerable<AnchorlessIdentityData> first, IEnumerable<AnchorlessIdentityData> second)
    {
        var merged = new List<AnchorlessIdentityData>();

        foreach (var item in first.Concat(second))
        {
            if (merged.Any(existing => existing.OriginalName == item.OriginalName))
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
