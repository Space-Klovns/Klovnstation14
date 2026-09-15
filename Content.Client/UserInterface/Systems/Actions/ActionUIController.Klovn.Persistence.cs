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

public sealed partial class ActionUIController
{
    [Dependency] private IBaseClient _baseClient = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IResourceManager _resourceManager = default!;

    private bool _hasLinkedActionSet;
    private bool _isLeavingServer;
    private bool _actionFoldersEnabled = true;
    private bool _actionLayoutPersistenceEnabled = true;
    private KsActionBarConfiguration? _pendingActionConfiguration;
    private HashSet<ActionPersistenceKey>? _lastAppliedActionIdentities;
    private HashSet<EntityUid>? _lastAppliedActionUids;


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
            true);
        _configurationManager.OnValueChanged(
            KsCCVars.ActionLayoutPersistenceEnabled,
            OnActionLayoutPersistenceEnabledChanged,
            true);
    }

    private void OnPlayerLeaveServer(object? sender, PlayerEventArgs args)
    {
        _isLeavingServer = true;
    }

    private void BeginLinkedActionConfiguration()
    {
        _hasLinkedActionSet = true;
        _isLeavingServer = false;
        _pendingActionConfiguration = null;
        _lastAppliedActionIdentities = null;
        _lastAppliedActionUids = null;
        TryRestoreActionConfiguration();
    }

    private void EndLinkedActionConfiguration()
    {
        _hasLinkedActionSet = false;
        _isLeavingServer = false;
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

    private void SaveActionConfigurationAfterChange()
    {
        if (_isLeavingServer || !_actionLayoutPersistenceEnabled)
            return;

        _pendingActionConfiguration = null;
        _lastAppliedActionIdentities = null;
        _lastAppliedActionUids = null;
        SaveActionConfiguration();
    }

    private bool SaveActionConfiguration()
    {
        if (!_actionLayoutPersistenceEnabled || !_hasLinkedActionSet || _actionsSystem == null)
            return false;

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

            using var writer = _resourceManager.UserData.OpenWriteText(GetActionConfigurationPath());
            writer.Write(KsActionBarConfigurationJson.Serialize(configuration));
            return true;
        }
        catch (Exception exception)
        {
            Log.Warning($"Could not save the action bar configuration: {exception.Message}");
            return false;
        }
    }

    private void TryRestoreActionConfiguration()
    {
        if (!_actionLayoutPersistenceEnabled || _actionsSystem == null)
            return;

        try
        {
            if (!_resourceManager.UserData.TryReadAllText(GetActionConfigurationPath(), out var json))
                return;

            if (!KsActionBarConfigurationJson.TryDeserialize(json, out var configuration) ||
                configuration.Version != KsActionBarConfiguration.CurrentVersion)
            {
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

    private bool TryApplyPendingActionConfiguration()
    {
        if (_pendingActionConfiguration == null)
            return false;

        var currentActions = _actionsSystem?.GetClientActions().Select(action => action.Owner).ToHashSet() ?? [];
        var currentIdentities = BuildActionIdentityMap().Values.Select(ToKey).ToHashSet();
        if (_lastAppliedActionUids == null ||
            !_lastAppliedActionUids.SetEquals(currentActions) ||
            _lastAppliedActionIdentities == null ||
            !_lastAppliedActionIdentities.SetEquals(currentIdentities))
        {
            ApplyActionConfiguration(_pendingActionConfiguration);
            _lastAppliedActionUids = currentActions;
            _lastAppliedActionIdentities = currentIdentities;
        }

        return true;
    }
    private void ApplyActionConfiguration(KsActionBarConfiguration configuration)
    {
        if (_actionsSystem == null)
            return;

        ResetActionFolders();
        var identities = BuildActionIdentityMap();
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

            var folderUid = CreateFolderAction(false, members[0]);
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

    private static bool TryResolveSavedAction(
        KsSavedActionIdentity saved,
        Dictionary<EntityUid, KsSavedActionIdentity> current,
        HashSet<EntityUid> assigned,
        out EntityUid actionUid)
    {
        // Prefer the exact provider-qualified identity whenever it exists.
        foreach (var (uid, identity) in current)
        {
            if (!assigned.Contains(uid) && ToKey(identity) == ToKey(saved))
            {
                assigned.Add(uid);
                actionUid = uid;
                return true;
            }
        }

        // Innate actions were saved without a provider. During their late grant the replicated Container can
        // temporarily look like an external provider, so fall back to their action prototype and occurrence.
        if (saved.ProviderPrototype == null)
        {
            foreach (var (uid, identity) in current)
            {
                if (!assigned.Contains(uid) && KsActionBarIdentity.MatchesSaved(saved, identity))
                {
                    assigned.Add(uid);
                    actionUid = uid;
                    return true;
                }
            }
        }

        actionUid = default;
        return false;
    }

    private Dictionary<EntityUid, KsSavedActionIdentity> BuildActionIdentityMap()
    {
        var result = new Dictionary<EntityUid, KsSavedActionIdentity>();
        if (_actionsSystem == null)
            return result;

        var occurrences = new Dictionary<(string ActionPrototype, string? ProviderPrototype), int>();
        foreach (var action in _actionsSystem.GetClientActions())
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

    private static ResPath GetActionConfigurationPath()
    {
        return new ResPath("/ks14_action_layout.json");
    }

    private static ActionPersistenceKey ToKey(KsSavedActionIdentity identity)
    {
        return new ActionPersistenceKey(
            identity.ActionPrototype,
            identity.ProviderPrototype,
            identity.Occurrence);
    }
}
