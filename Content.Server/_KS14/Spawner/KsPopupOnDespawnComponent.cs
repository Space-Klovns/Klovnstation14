using Content.Shared.Popups;

namespace Content.Server._KS14.Spawner;

/// <summary>
///     Shows a popup where the entity is when its <c>TimedDespawn</c> runs out, to everyone nearby. For telegraphs that
///         spawn something as they go (<c>SpawnOnDespawn</c>), so the arrival is announced.
/// </summary>
[RegisterComponent]
[Access(typeof(KsPopupOnDespawnSystem))]
public sealed partial class KsPopupOnDespawnComponent : Component
{
    [DataField(required: true)]
    public LocId Message;

    [DataField]
    public PopupType PopupType = PopupType.MediumCaution;
}
