using System.Collections.Generic;

namespace Content.Client._KS14.Actions;

/// <summary>
///     Stable, client-local representation of an action bar and its one-level folders.
/// </summary>
public sealed class KsActionBarConfiguration
{
    /// <summary>
    ///     Schema version this build writes.
    /// </summary>
    /// <remarks>
    ///     Version 1 was the pre-folder layout, which stored bare slot-to-prototype pairs and had no
    ///         <see cref="KnownActions"/>. There is no migration: a version 1 file cannot say whether an
    ///         absent action was removed on purpose, so it is discarded and the bar auto-populates.
    /// </remarks>
    public const int CurrentVersion = 2;

    /// <summary>
    ///     Version of the schema this particular layout was written with.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    ///     The bar itself, in slot order. One entry per slot.
    /// </summary>
    public List<KsActionBarConfigurationEntry> Entries { get; set; } = [];

    /// <summary>
    ///     Every action the player had when this layout was saved, placed or not.
    /// </summary>
    /// <remarks>
    ///     This is what makes deliberate removal stick. An action listed here but absent from
    ///         <see cref="Entries"/> was taken off the bar on purpose and is not auto-populated back on;
    ///         an action in neither is simply new, and is.
    /// </remarks>
    public List<KsSavedActionIdentity> KnownActions { get; set; } = [];
}

/// <summary>
///     One slot of a saved bar: either a single action or a folder of them.
/// </summary>
/// <remarks>
///     Exactly one of <see cref="Action"/> and <see cref="Folder"/> is expected to be set. An entry with
///         neither is skipped on read.
/// </remarks>
public sealed class KsActionBarConfigurationEntry
{
    /// <summary>
    ///     The action in this slot, if the slot holds a single action.
    /// </summary>
    public KsSavedActionIdentity? Action { get; set; }

    /// <summary>
    ///     The actions in this slot, if the slot holds a folder.
    /// </summary>
    public List<KsSavedActionIdentity>? Folder { get; set; }
}

/// <summary>
///     Identifies one action across sessions, since entity uids do not survive a reconnect.
/// </summary>
public sealed class KsSavedActionIdentity
{
    /// <summary>
    ///     Prototype id of the action entity.
    /// </summary>
    public string ActionPrototype { get; set; } = string.Empty;

    /// <summary>
    ///     Prototype id of the item granting the action, or null if the action is innate.
    /// </summary>
    /// <remarks>
    ///     Part of the identity so that an item's action cannot take over an innate action's slot just
    ///         because both come from the same action prototype.
    /// </remarks>
    public string? ProviderPrototype { get; set; }

    /// <summary>
    ///     Which of several otherwise identical actions this is, zero-based.
    /// </summary>
    /// <remarks>
    ///     Two of the same item grant two actions with the same prototype and the same provider; this is
    ///         what tells them apart. Assigned in
    ///         <see cref="Content.Client.Actions.ActionsSystem.ActionComparer"/> order, because an
    ///         unstable order would swap the two items' slots from one session to the next.
    /// </remarks>
    public int Occurrence { get; set; }
}
