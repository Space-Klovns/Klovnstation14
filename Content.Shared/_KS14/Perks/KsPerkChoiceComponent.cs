using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Perks;

/// <summary>
///     On a body whose player has been asked to pick a perk and hasn't yet. While it is there the picker stays open:
///         it has no way to close, and the server opens it again if it closes anyway.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedKsPerkChoiceSystem))]
public sealed partial class KsPerkChoiceComponent : Component
{
    /// <summary>
    ///     The perks offered. The picker shows these, and the server accepts only one of them.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public List<ProtoId<KsPerkPrototype>> Options = new();

    /// <summary>
    ///     What asked, such as a game rule, passed on in <see cref="KsPerkChosenEvent"/>. Server only.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? Source;
}

[Serializable, NetSerializable]
public enum KsPerkChoiceUiKey : byte
{
    Key,
}

/// <summary>
///     The player picked a perk. Checked against <see cref="KsPerkChoiceComponent.Options"/> on the server.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsPerkChoiceSelectMessage(ProtoId<KsPerkPrototype> perk) : BoundUserInterfaceMessage
{
    public readonly ProtoId<KsPerkPrototype> Perk = perk;
}

/// <summary>
///     Raised on the body, and broadcast, once its player has picked a valid perk. Whoever asked (see
///         <see cref="Source"/>) stores and applies the choice and sets <see cref="Handled"/>; if nobody does, the perk
///         is simply given to the body.
/// </summary>
[ByRefEvent]
public record struct KsPerkChosenEvent(EntityUid Body, EntityUid Actor, ProtoId<KsPerkPrototype> Perk, EntityUid? Source)
{
    public bool Handled;
}
