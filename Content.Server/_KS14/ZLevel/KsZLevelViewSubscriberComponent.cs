namespace Content.Server._KS14.ZLevel;

/// <summary>
///     Added to virtual entities that are loading in (to PVS) things on z-levels for people above that z-level
///         to see.
///
///     This entire entity won't exist without an existing <see cref="ViewerUid"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(KsZLevelPvsSystem))]
public sealed partial class KsZLevelViewSubscriberComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public EntityUid ViewerUid = EntityUid.Invalid;
}
