using Content.Shared.Storage.Components;
using Content.Shared.Whitelist;

namespace Content.Shared._KS14.Storage;

/// <summary>
///     On an <see cref="EntityStorageComponent"/> entity, such as a closet: something thrown into it while it stands
///         open lands inside, and the door shuts on it. The storage still decides what fits (size, capacity, its own
///         whitelist); what doesn't is left outside the shut door.
/// </summary>
[RegisterComponent]
public sealed partial class KsEntityStorageCatchThrownComponent : Component
{
    /// <summary>
    ///     What is caught. Null catches anything thrown that the storage would take.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist = new()
    {
        Components =
        [
            "MobState",
        ],
    };
}
