using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Perks;

/// <summary>
///     The perks a body holds, and exactly what each one gave it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedKsPerkSystem))]
public sealed partial class KsPerkHolderComponent : Component
{
    /// <summary>
    ///     The perks held, in the order they were given. Networked, so shared code can ask
    ///         <see cref="SharedKsPerkSystem.HasPerk"/> on the client too.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public List<ProtoId<KsPerkPrototype>> Perks = new();

    /// <summary>
    ///     What each held perk added, so taking it away undoes exactly that. Server only. Read-only in VV: replacing a
    ///         grant would leave what it gave on the body for good.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Dictionary<ProtoId<KsPerkPrototype>, KsPerkGrant> Grants = new();
}

/// <summary>
///     What one perk added to a body: only what wasn't there already.
/// </summary>
public sealed class KsPerkGrant
{
    /// <summary>
    ///     Names of the components added.
    /// </summary>
    [ViewVariables]
    public readonly List<string> Components = new();

    [ViewVariables]
    public readonly List<KsPerkStatusEffectGrant> StatusEffects = new();

    /// <summary>
    ///     The action entities given.
    /// </summary>
    [ViewVariables]
    public readonly List<EntityUid> Actions = new();

    /// <summary>
    ///     The items spawned, and not since taken back.
    /// </summary>
    [ViewVariables]
    public readonly List<EntityUid> Items = new();

    /// <summary>
    ///     Whether the items were taken back while the perk stays held (the holder died or left the body), to be given
    ///         again if they come back to it.
    /// </summary>
    [ViewVariables]
    public bool ItemsWithdrawn;
}

/// <summary>
///     A status effect a perk made permanent.
/// </summary>
/// <param name="Effect">The status effect's prototype.</param>
/// <param name="Added">Whether the perk added it, rather than extending one already running.</param>
/// <param name="PreviousEndTime">When the one already running would have ended, to put back.</param>
public readonly record struct KsPerkStatusEffectGrant(EntProtoId Effect, bool Added, TimeSpan? PreviousEndTime);

/// <summary>
///     Raised on a body after a perk's grants have been applied to it.
/// </summary>
[ByRefEvent]
public readonly record struct KsPerkAddedEvent(ProtoId<KsPerkPrototype> Perk);

/// <summary>
///     Raised on a body after a perk's grants have been taken back from it.
/// </summary>
[ByRefEvent]
public readonly record struct KsPerkRemovedEvent(ProtoId<KsPerkPrototype> Perk);
