namespace Content.Client._KS14.Carrying;

/// <summary>
///     On someone carried, while they are drawn above their carrier: what to put back when they are put down.
/// </summary>
[RegisterComponent, Access(typeof(KsFiremanCarryVisualsSystem))]
public sealed partial class KsCarriedVisualsComponent : Component
{
    /// <summary>
    ///     Their sprite's draw depth before they were lifted.
    /// </summary>
    [ViewVariables]
    public int OriginalDrawDepth;
}
