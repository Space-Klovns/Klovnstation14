namespace Content.Server._KS14.SPAM;

/// <summary>
///     On a stack of ore: how many of its units a S.P.A.M. has already counted toward a task. A unit counts once, so
///         the number follows the units when stacks merge or split.
/// </summary>
[RegisterComponent]
[Access(typeof(KsSPAMOreSystem))]
public sealed partial class KsSPAMCountedOreComponent : Component
{
    [DataField]
    public int Counted;
}
