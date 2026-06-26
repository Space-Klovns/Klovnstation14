using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Definition;

namespace Content.Shared._KS14.Trigger.Components;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class AnnouncementOnTriggerComponent : Component
{
    /// <summary>
    ///     When receiving a key, will do the corresponding
    ///         announcement.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, AnnouncementOnTriggerDatum> AnnouncementsPerKey = [];
}

[Serializable, NetSerializable]
[DataDefinition]
public sealed partial class AnnouncementOnTriggerDatum
{
    [DataField(required: true)]
    public LocId AnnouncementLoc;

    [DataField]
    public LocId? SenderLoc = null;

    [DataField]
    public Color? ColorOverride = null;

    [DataField]
    public SoundSpecifier? Sound = null;
}
