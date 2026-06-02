using Robust.Shared.Player;

namespace Content.Server._KS14.ZLevel;

/// <summary>
///     This component won't exist without an existing <see cref="ViewSubscriberUid"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(KsZLevelPvsSystem))]
public sealed partial class KsZLevelViewerComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public ICommonSession Session;

    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid ViewSubscriberUid;
}
