using Content.Shared.RCD.Systems;

namespace Content.Shared._KS14.RCD;

/// <summary>
///     Something built by an instant RCD, and who built it. Its builder can take it apart again with an instant RCD
///         straight away; anyone else takes the normal time.
/// </summary>
[RegisterComponent, Access(typeof(RCDSystem))]
public sealed partial class KsRcdBuiltComponent : Component
{
    [ViewVariables]
    public EntityUid? Builder;
}
