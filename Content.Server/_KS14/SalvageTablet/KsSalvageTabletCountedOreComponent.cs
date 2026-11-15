namespace Content.Server._KS14.SalvageTablet;

/// <summary>
///     On a stack of ore: how many of its units a S.P.A.M. has already counted toward a task. A unit counts once, so
///         the number follows the units when stacks merge or split.
/// </summary>
[RegisterComponent]
[Access(typeof(KsSalvageTabletOreSystem))]
public sealed partial class KsSalvageTabletCountedOreComponent : Component
{
    [DataField]
    public int Counted;
}
