using Content.Shared.Alert;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Perks;

/// <summary>
///     A perk: a set of things given to a body while it holds the perk, and taken back exactly when it loses it
///         (<see cref="SharedKsPerkSystem.TryAddPerk"/>). Anything already on the body before stays.
/// </summary>
/// <remarks>
///     An ability is a component listed in <see cref="Components"/>, with a system of its own reacting to it; see
///         <c>Klovn/Docs/perks.md</c>.
/// </remarks>
[Prototype]
public sealed partial class KsPerkPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    /// <summary>
    ///     Shown in the picker.
    /// </summary>
    [DataField(required: true)]
    public SpriteSpecifier Icon = SpriteSpecifier.Invalid;

    /// <summary>
    ///     Shown to the holder while they hold the perk.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype>? Alert;

    /// <summary>
    ///     Added to the body, unless it already has them.
    /// </summary>
    [DataField]
    public ComponentRegistry Components = new();

    /// <summary>
    ///     Status effects made permanent on the body for as long as it holds the perk.
    /// </summary>
    [DataField]
    public List<EntProtoId> StatusEffects = new();

    /// <summary>
    ///     Actions given to the body, one instance each.
    /// </summary>
    [DataField]
    public List<EntProtoId> Actions = new();

    /// <summary>
    ///     Items spawned into the body's hands, or at its feet. Deleted when the perk is taken away, wherever they are.
    /// </summary>
    [DataField]
    public List<EntProtoId> Items = new();
}
