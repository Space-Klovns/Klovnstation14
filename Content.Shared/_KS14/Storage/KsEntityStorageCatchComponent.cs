using Content.Shared._KS14.KsPopup;
using Content.Shared.Storage.Components;
using Content.Shared.Whitelist;

namespace Content.Shared._KS14.Storage;

/// <summary>
///     On an <see cref="EntityStorageComponent"/> entity, such as a closet: someone thrown or shoved into it while it
///         stands open lands inside, and the door shuts on them. The storage still decides what fits (size, capacity,
///         its own whitelist); what doesn't is left outside the shut door.
///     Thrown things are caught by any such storage. To catch the shoved it must also be something to be shoved
///         into, a <c>KsShoveObstacle</c>.
/// </summary>
[RegisterComponent]
public sealed partial class KsEntityStorageCatchComponent : Component
{
    /// <summary>
    ///     What is caught. Null catches anything the storage would take.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist = new()
    {
        Components =
        [
            "MobState",
        ],
    };

    /// <summary>
    ///     Popups when someone sends another into the storage. Each gets <c>user</c>, <c>target</c> and
    ///         <c>storage</c>. Nothing is shown when nobody sent them.
    /// </summary>
    [DataField]
    public KsAudiencePopups Popups = new("ks-storage-catch-user", "ks-storage-catch-target", "ks-storage-catch-others");
}
