using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.McqDialogue;

/// <summary>
///     Added to a dialogue entity by <see cref="McqDialogueSettings.HeldItemWhitelist"/>: the dialogue closes once the item its user held in their active hand when it opened is no
///         longer there, and can't be answered after that. A user holding nothing that passes
///         <see cref="Whitelist"/> when it opens gets it closed straight away.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(McqDialogueHeldItemSystem), typeof(McqDialogueSystem), Other = AccessPermissions.Read)]
public sealed partial class McqDialogueHeldItemComponent : Component
{
    /// <summary>
    ///     What the held item has to be. Null for anything.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    ///     The item the user held when the dialogue opened, if it passed <see cref="Whitelist"/>. Networked for the
    ///         source to read on the answering client, predicting what the answer does.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? HeldUid;
}
