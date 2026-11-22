namespace Content.Client._KS14.Standing;

/// <summary>
///     The depth a mob drew at before <see cref="KsDownedDrawDepthSystem"/> lowered it, there only while it is lowered.
/// </summary>
[RegisterComponent, Access(typeof(KsDownedDrawDepthSystem))]
public sealed partial class KsDownedDrawDepthComponent : Component
{
    [ViewVariables]
    public int OriginalDrawDepth;
}
