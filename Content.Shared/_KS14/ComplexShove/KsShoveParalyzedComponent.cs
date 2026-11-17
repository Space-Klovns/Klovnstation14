using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ComplexShove;

/// <summary>
///     Someone kicked onto their side by a shove while down. Until they stand up again, more shoves can't paralyze
///         them, so the paralysis can be neither chained nor extended. The next shove makes them drop everything they
///         hold, once; after that, shoves only push them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsShoveParalyzedComponent : Component
{
    /// <summary>
    ///     Whether a shove has already made them drop what they hold.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Disarmed;
}
