// KS14: added in this fork
using System.Collections.Generic;
using System.Linq;
using Content.Client._KS14.Actions;
using Content.Shared.Actions.Components;
using Content.Shared._KS14.CCVar;
using Robust.Client;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Actions;

/// <summary>
///     Remembers where the player put each action, and puts them back there next time.
/// </summary>
/// <remarks>
///     Actions are matched across sessions by (action prototype, providing item prototype, occurrence)
///         rather than by uid, because uids do not survive a reconnect.
///     An action recorded in <see cref="KsActionBarConfiguration.KnownActions"/> but absent from
///         <see cref="KsActionBarConfiguration.Entries"/> was taken off the bar on purpose, so it stays
///         off. Anything the saved layout has never seen auto-populates as usual.
///     Known limitation: the layout is one file per client install, scoped to neither server nor
///         character. The engine offers no stable server identity to key it on - <c>ServerInfo</c> only
///         carries a mutable display name - and keying it on a character name would break on a rename.
/// </remarks>
public sealed partial class ActionUIController
{
    /// <summary>
    ///     Where the layout is kept, under the player's own data directory.
    /// </summary>
    private static readonly ResPath ActionConfigurationPath = new("/ks14_action_layout.json");

    [Dependency] private IBaseClient _baseClient = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IResourceManager _resourceManager = default!;

    private bool _hasLinkedActionSet;
    private bool _isLeavingServer;
    private bool _actionFoldersEnabled = true;
    private bool _actionLayoutPersistenceEnabled = true;

    /// <summary>
    ///     Set when something changed the layout, cleared once it has been written out.
    /// </summary>
    /// <remarks>
    ///     Exists so that equipping an item which grants several actions writes the file once at the end
    ///         of the frame instead of once per action, synchronously, on the UI thread.
    /// </remarks>
    private bool _actionConfigurationDirty;

    private KsActionBarConfiguration? _pendingActionConfiguration;
    private HashSet<ActionPersistenceKey>? _lastAppliedActionIdentities;
    private HashSet<EntityUid>? _lastAppliedActionUids;

    /// <summary>
    ///     Identity of one action across sessions.
    /// </summary>
    /// <param name="ActionPrototype">Prototype of the action entity itself.</param>
    /// <param name="ProviderPrototype">Prototype of the item granting it, or null if innate.</param>
    /// <param name="Occurrence">
    ///     Which of several otherwise identical actions this is, counted in a stable sort order.
    /// </param>
    private readonly record struct ActionPersistenceKey(
        string ActionPrototype,
        string? ProviderPrototype,
        int Occurrence);

    private void InitializeActionConfigurationPersistence()
    {
        _baseClient.PlayerLeaveServer += OnPlayerLeaveServer;
        _configurationManager.OnValueChanged(
            KsCCVars.ActionFoldersEnabled,
            OnActionFoldersEnabledChanged,
            invokeImmediately: true);
        _configurationManager.OnValueChanged(
            KsCCVars.ActionLayoutPersistenceEnabled,
            OnActionLayoutPersistenceEnabledChanged,
            invokeImmediately: true);
    }

    private void OnPlayerLeaveServer(object? sender, PlayerEventArgs args)
    {
        _isLeavingServer = true;
    }

    private void BeginLinkedActionConfiguration()
    {
        _hasLinkedActionSet = true;
        _isLeavingServer = false;
        _actionConfigurationDirty = false;
        _pendingActionConfiguration = null;
        _lastAppliedActionIdentities = null;
        _lastAppliedActionUids = null;
        TryRestoreActionConfiguration();
    }

    /// <summary>
    ///     Flushes anything still unsaved, then forgets the action set that was linked.
    /// </summary>
    private void EndLinkedActionConfiguration()
    {
        FlushPendingActionConfigurationSave();

        _hasLinkedActionSet = false;
        _isLeavingServer = false;
        _actionConfigurationDirty = false;
        _pendingActionConfiguration = null;
        _lastAppliedActionIdentities = null;
        _lastAppliedActionUids = null;
    }

    private void OnActionFoldersEnabledChanged(bool enabled)
    {
        _actionFoldersEnabled = enabled;
        if (enabled || !_hasLinkedActionSet)
            return;

        _pendingActionConfiguration = null;
        FlattenActionFolders();
        RefreshActionBar();
        SaveActionConfigurationAfterChange();
    }

    private void OnActionLayoutPersistenceEnabledChanged(bool enabled)
    {
        _actionLayoutPersistenceEnabled = enabled;
        _pendingActionConfiguration = null;
        if (enabled && _hasLinkedActionSet)
            SaveActionConfigurationAfterChange();
    }

    /// <summary>
    ///     Marks the layout as needing a write. The write itself happens at the end of the frame.
    /// </summary>
    private void SaveActionConfigurationAfterChange()
    {
        if (_isLeavingServer || !_actionLayoutPersistenceEnabled)
            return;

        _pendingActionConfiguration = null;
        _lastAppliedActionIdentities = null;
        _lastAppliedActionUids = null;
        _actionConfigurationDirty = true;
    }

    /// <summary>
    ///     Writes the layout out if anything has changed since the last write.
    /// </summary>
    private void FlushPendingActionConfigurationSave()
    {
        if (!_actionConfigurationDirty)
            return;

        _actionConfigurationDirty = false;
        SaveActionConfiguration();
    }

    private void SaveActionConfiguration()
    {
        if (!_actionLayoutPersistenceEnabled || !_hasLinkedActionSet || _actionsSystem == null)
            return;

        try
        {
            SyncOpenActionFolder();
            var identities = BuildActionIdentityMap();
            var configuration = new KsActionBarConfiguration
            {
                KnownActions = identities.Values.ToList(),
            };

            var rootActions = _rootFolderActions ?? _actions;
            foreach (var actionUid in rootActions)
            {
                if (actionUid is not { } uid)
                    continue;

                if (EntityManager.TryGetComponent<KsActionFolderComponent>(uid, out var folder))
                {
                    var members = new List<KsSavedActionIdentity>();
                    foreach (var memberUid in folder.Actions)
                    {
                        if (identities.TryGetValue(memberUid, out var memberIdentity))
                            members.Add(memberIdentity);
                    }

                    if (members.Count > 0)
                        configuration.Entries.Add(new KsActionBarConfigurationEntry { Folder = members });

                    continue;
                }

                if (identities.TryGetValue(uid, out var identity))
                    configuration.Entries.Add(new KsActionBarConfigurationEntry { Action = identity });
            }

            using var writer = _resourceManager.UserData.OpenWriteText(ActionConfigurationPath);
            writer.Write(KsActionBarConfigurationJson.Serialize(configuration));
        }
        catch (Exception exception)
        {
            Log.Warning($"Could not save the action bar configuration: {exception.Message}");
        }
    }

    private void TryRestoreActionConfiguration()
    {
        if (!_actionLayoutPersistenceEnabled || _actionsSystem == null)
            return;

        try
        {
            if (!_resourceManager.UserData.TryReadAllText(ActionConfigurationPath, out var json))
                return;

            if (!KsActionBarConfigurationJson.TryDeserialize(json, out var configuration))
            {
                Log.Warning("The saved action bar layout could not be read, and has been ignored.");
                return;
            }

            if (configuration.Version != KsActionBarConfiguration.CurrentVersion)
            {
                Log.Info(
                    $"Ignoring a saved action bar layout written by version {configuration.Version}; " +
                    $"this build writes version {KsActionBarConfiguration.CurrentVersion} and has no migration for it.");
                return;
            }

            _pendingActionConfiguration = configuration;
            _lastAppliedActionIdentities = null;
            _lastAppliedActionUids = null;
            TryApplyPendingActionConfiguration();
        }
        catch (Exception exception)
        {
            Log.Warning($"Could not restore the action bar configuration: {exception.Message}");
        }
    }

    /// <summary>
    ///     Re-applies the saved layout, if one is still pending and the live action set has moved on
    ///         since it was last applied.
    /// </summary>
    private bool TryApplyPendingActionConfiguration()
    {
        if (_pendingActionConfiguration == null)
            return false;

        var currentActions = _actionsSystem?.GetClientActions().Select(action => action.Owner).ToHashSet() ?? [];
        var identities = BuildActionIdentityMap();
        var currentIdentities = identities.Values.Select(ToKey).ToHashSet();

        if (_lastAppliedActionUids == null ||
            !_lastAppliedActionUids.SetEquals(currentActions) ||
            _lastAppliedActionIdentities == null ||
            !_lastAppliedActionIdentities.SetEquals(currentIdentities))
        {
            ApplyActionConfiguration(_pendingActionConfiguration, identities);
            _lastAppliedActionUids = currentActions;
            _lastAppliedActionIdentities = currentIdentities;
        }

        return true;
    }

    /// <summary>
    ///     Rebuilds the whole bar from a saved layout.
    /// </summary>
    /// <param name="identities">
    ///     Identity map for the live action set, built by the caller so it is not built twice per apply.
    /// </param>
    private void ApplyActionConfiguration(
        KsActionBarConfiguration configuration,
        Dictionary<EntityUid, KsSavedActionIdentity> identities)
    {
        if (_actionsSystem == null)
            return;

        ResetActionFolders();
        var assigned = new HashSet<EntityUid>();

        _actions.Clear();
        foreach (var entry in configuration.Entries)
        {
            if (entry.Action != null &&
                TryResolveSavedAction(entry.Action, identities, assigned, out var actionUid))
            {
                _actions.Add(actionUid);
                continue;
            }

            if (entry.Folder == null)
                continue;

            var members = new List<EntityUid>();
            foreach (var savedMember in entry.Folder)
            {
                if (TryResolveSavedAction(savedMember, identities, assigned, out var memberUid))
                {
                    members.Add(memberUid);
                }
            }

            if (members.Count == 0)
                continue;

            if (!_actionFoldersEnabled)
            {
                _actions.AddRange(members.Cast<EntityUid?>());
                continue;
            }

            var folderUid = CreateFolderAction(isExit: false, members[0]);
            EntityManager.GetComponent<KsActionFolderComponent>(folderUid).Actions.AddRange(members);
            _actions.Add(folderUid);
        }

        // New and non-persistable actions should still auto-populate in the normal deterministic order. Actions
        // which existed when the layout was saved but were intentionally removed remain absent.
        var currentActions = _actionsSystem.GetClientActions().ToList();
        currentActions.Sort(Content.Client.Actions.ActionsSystem.ActionComparer);
        foreach (var action in currentActions)
        {
            if (assigned.Contains(action.Owner) || !action.Comp.AutoPopulate)
                continue;

            if (identities.TryGetValue(action.Owner, out var identity) &&
                configuration.KnownActions.Any(saved => KsActionBarIdentity.MatchesSaved(saved, identity)))
            {
                continue;
            }

            _actions.Add(action.Owner);
        }
    }

    /// <summary>
    ///     Finds the live action matching a saved identity, skipping ones already claimed by an earlier
    ///         entry.
    /// </summary>
    private static bool TryResolveSavedAction(
        KsSavedActionIdentity saved,
        Dictionary<EntityUid, KsSavedActionIdentity> current,
        HashSet<EntityUid> assigned,
        out EntityUid actionUid)
    {
        // Provider identity is part of the key so an earlier item action cannot occupy an intrinsic action's slot.
        foreach (var (uid, identity) in current)
        {
            if (!assigned.Contains(uid) && ToKey(identity) == ToKey(saved))
            {
                assigned.Add(uid);
                actionUid = uid;
                return true;
            }
        }

        actionUid = default;
        return false;
    }

    /// <summary>
    ///     Builds the cross-session identity of every action the local player currently has.
    /// </summary>
    /// <remarks>
    ///     Iterated in <see cref="Content.Client.Actions.ActionsSystem.ActionComparer"/> order rather
    ///         than in enumeration order: <c>Occurrence</c> disambiguates otherwise identical actions by
    ///         position, so an unstable order would swap two identical items' slots between sessions.
    /// </remarks>
    private Dictionary<EntityUid, KsSavedActionIdentity> BuildActionIdentityMap()
    {
        var result = new Dictionary<EntityUid, KsSavedActionIdentity>();
        if (_actionsSystem == null)
            return result;

        var sortedActions = _actionsSystem.GetClientActions().ToList();
        sortedActions.Sort(Content.Client.Actions.ActionsSystem.ActionComparer);

        var occurrences = new Dictionary<(string ActionPrototype, string? ProviderPrototype), int>();
        foreach (var action in sortedActions)
        {
            if (!EntityManager.TryGetComponent<MetaDataComponent>(action.Owner, out var actionMetadata) ||
                actionMetadata.EntityPrototype?.ID is not { } actionPrototype)
            {
                continue;
            }

            string? providerPrototype = null;
            // AttachedEntity can still be null while the initial replicated action set is being assembled.
            // GetClientActions() is already scoped to the local player, so use that stable owner when deciding
            // whether Container is an external provider. Otherwise innate actions are temporarily keyed as if
            // the player's prototype provided them and miss their saved slots.
            if (action.Comp.Container is { } provider && provider != _playerManager.LocalEntity &&
                EntityManager.TryGetComponent<MetaDataComponent>(provider, out var providerMetadata))
            {
                providerPrototype = providerMetadata.EntityPrototype?.ID;
            }

            var baseIdentity = (actionPrototype, providerPrototype);
            occurrences.TryGetValue(baseIdentity, out var occurrence);
            occurrences[baseIdentity] = occurrence + 1;
            result[action.Owner] = new KsSavedActionIdentity
            {
                ActionPrototype = actionPrototype,
                ProviderPrototype = providerPrototype,
                Occurrence = occurrence,
            };
        }

        return result;
    }

    private static ActionPersistenceKey ToKey(KsSavedActionIdentity identity)
    {
        return new ActionPersistenceKey(
            identity.ActionPrototype,
            KsActionBarIdentity.NormalizeProvider(identity.ProviderPrototype),
            identity.Occurrence);
    }
}
