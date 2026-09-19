using System.Collections.Generic;

namespace Content.Client._KS14.Actions;

/// <summary>
///     Marks a nullspace entity as standing in for an action bar folder, and holds what is in it.
/// </summary>
/// <remarks>
///     Deliberately carries no datafields and no networking. Folders are a client-side arrangement of
///         the player's own bar: the server neither knows nor cares about them, and they are rebuilt from
///         the saved layout on each connect rather than being saved with the entity.
/// </remarks>
[RegisterComponent]
public sealed partial class KsActionFolderComponent : Component
{
    /// <summary>
    ///     The actions inside this folder, in the order they are shown.
    /// </summary>
    public readonly List<EntityUid> Actions = [];

    /// <summary>
    ///     Whether this is the "back" entry shown in the first slot of an open folder, rather than a
    ///         folder proper. The back entry holds no actions and cannot be edited.
    /// </summary>
    public bool IsExit;
}
