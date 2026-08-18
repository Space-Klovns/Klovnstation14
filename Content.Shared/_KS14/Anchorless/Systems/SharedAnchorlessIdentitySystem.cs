using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._KS14.Anchorless.Components;
using Content.Shared.Body;
using Content.Shared.Cloning;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._KS14.Anchorless.Systems;

/// <summary>
/// Retains an Anchorless' identities in nullspace and synchronizes them to its owner.
/// Communion intentionally transfers memories both ways; it never consumes a victim.
/// </summary>
public abstract partial class SharedAnchorlessIdentitySystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedCloningSystem _cloning = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedPvsOverrideSystem _pvs = default!;
    [Dependency] private SharedVisualBodySystem _visualBody = default!;
    [Dependency] private IdentitySystem _identity = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    private MapId? _pausedMap;

    public override void Initialize()
    {
        SubscribeLocalEvent<AnchorlessComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AnchorlessComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<AnchorlessComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<AnchorlessComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<AnchorlessComponent, AnchorlessTransformActionEvent>(OnTransform);
        SubscribeLocalEvent<AnchorlessComponent, AnchorlessCommunionActionEvent>(OnCommunion);
        SubscribeLocalEvent<AnchorlessComponent, AnchorlessTransformIdentitySelectMessage>(OnTransformSelected);
    }

    private void OnMapInit(Entity<AnchorlessComponent> ent, ref MapInitEvent args)
    {
        var ui = EnsureComp<UserInterfaceComponent>(ent);
        _ui.SetUi((ent, ui), AnchorlessTransformUiKey.Key, new InterfaceData("AnchorlessTransformBoundUserInterface"));

        if (_net.IsClient)
            return;

        var identity = GrantIdentity(ent, ent.Owner);
        if (identity == null || !TryGetDataFromStoredIdentity(ent, identity.Value, out var data))
            return;

        data.Starting = true;
        ent.Comp.CurrentIdentity = identity;
        Dirty(ent);
    }

    private void OnShutdown(Entity<AnchorlessComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<ActorComponent>(ent, out var actor))
            RemovePvsOverrides(ent, actor.PlayerSession);

        if (_net.IsClient)
            return;

        foreach (var data in ent.Comp.LearnedIdentities)
            QueueDel(data.StoredIdentity);
    }

    private void OnPlayerAttached(Entity<AnchorlessComponent> ent, ref PlayerAttachedEvent args)
    {
        foreach (var data in ent.Comp.LearnedIdentities)
            if (data.StoredIdentity != null)
                _pvs.AddSessionOverride(data.StoredIdentity.Value, args.Player);
    }

    private void OnPlayerDetached(Entity<AnchorlessComponent> ent, ref PlayerDetachedEvent args)
        => RemovePvsOverrides(ent, args.Player);

    private void RemovePvsOverrides(Entity<AnchorlessComponent> ent, Robust.Shared.Player.ICommonSession session)
    {
        foreach (var data in ent.Comp.LearnedIdentities)
            if (data.StoredIdentity != null)
                _pvs.RemoveSessionOverride(data.StoredIdentity.Value, session);
    }

    private void OnCommunion(Entity<AnchorlessComponent> ent, ref AnchorlessCommunionActionEvent args)
    {
        if (args.Handled || !TryComp<AnchorlessComponent>(ent, out var self) ||
            !TryComp<AnchorlessComponent>(args.Target, out var other))
            return;

        args.Handled = true;
        _popupSystem.PopupEntity(Loc.GetString("anchorless-communion-message"), ent.Owner, ent.Owner, PopupType.Medium);
        if (_net.IsClient)
            return;

        // Snapshot first: identities learned during this communion must be given to both participants.
        var memories = self.LearnedIdentities.Concat(other.LearnedIdentities).ToList();
        LearnMissing((ent.Owner, self), memories);
        LearnMissing((args.Target, other), memories);
    }

    private void OnTransform(Entity<AnchorlessComponent> ent, ref AnchorlessTransformActionEvent args)
    {
        if (args.Handled || !TryComp<UserInterfaceComponent>(ent, out var ui))
            return;

        args.Handled = true;
        if (!_ui.IsUiOpen((ent, ui), AnchorlessTransformUiKey.Key, args.Performer))
            _ui.OpenUi((ent, ui), AnchorlessTransformUiKey.Key, args.Performer);
    }

    private void OnTransformSelected(Entity<AnchorlessComponent> ent, ref AnchorlessTransformIdentitySelectMessage args)
    {
        if (!TryGetEntity(args.TargetIdentity, out var target) || ent.Comp.CurrentIdentity == target)
            return;

        if (!TryGetDataFromStoredIdentity(ent, target.Value, out _))
            return;

        TransformInto(ent, target.Value);
    }

    /// <summary>
    /// Applies an Anchorless stored identity using the same visual-body and component cloning path
    /// as every other Anchorless transformation. Inventory is deliberately not cloned.
    /// </summary>
    public void TransformInto(Entity<AnchorlessComponent> ent, EntityUid target)
    {
        _visualBody.CopyAppearanceFrom(target, ent.Owner);
        _cloning.CloneComponents(target, ent.Owner, ent.Comp.IdentityCloningSettings);
        _meta.SetEntityName(ent, Name(target), raiseEvents: false);
        _identity.QueueIdentityUpdate(ent);
        ent.Comp.CurrentIdentity = target;
        Dirty(ent);
    }

    private void LearnMissing(Entity<AnchorlessComponent> recipient, IEnumerable<AnchorlessIdentityData> memories)
    {
        foreach (var memory in memories)
        {
            if (memory.StoredIdentity == null || HasOriginal(recipient, memory.OriginalEntity, memory.OriginalName))
                continue;

            GrantIdentity(recipient, memory.StoredIdentity.Value, memory.OriginalEntity, memory.OriginalName, memory.Starting);
        }
    }

    /// <summary>Preserve a new identity, optionally retaining the original person's identity key.</summary>
    public EntityUid? GrantIdentity(Entity<AnchorlessComponent> recipient, EntityUid source,
        EntityUid? original = null, string? originalName = null, bool starting = false)
    {
        if (_net.IsClient)
            return null;

        original ??= source;
        originalName ??= Name(source);
        if (HasOriginal(recipient, original, originalName))
            return null;

        EnsurePausedMap();
        if (_pausedMap == null || !_cloning.TryCloning(source, new MapCoordinates(0, 0, _pausedMap.Value), recipient.Comp.IdentityCloningSettings, out var clone))
            return null;

        recipient.Comp.LearnedIdentities.Add(new AnchorlessIdentityData
        {
            StoredIdentity = clone,
            OriginalEntity = original,
            OriginalName = originalName,
            Starting = starting,
        });

        if (TryComp<ActorComponent>(recipient, out var actor))
            _pvs.AddSessionOverride(clone.Value, actor.PlayerSession);

        Dirty(recipient);
        return clone;
    }

    private bool HasOriginal(Entity<AnchorlessComponent> ent, EntityUid? original, string name)
        => ent.Comp.LearnedIdentities.Any(x => original != null && x.OriginalEntity == original || original == null && x.OriginalEntity == null && x.OriginalName == name);

    private void EnsurePausedMap()
    {
        if (_map.MapExists(_pausedMap))
            return;

        var map = _map.CreateMap(out var id);
        _meta.SetEntityName(map, "Anchorless identities");
        _map.SetPaused(map, true);
        _pausedMap = id;
    }

    public bool TryGetDataFromStoredIdentity(Entity<AnchorlessComponent> ent, EntityUid stored,
        [NotNullWhen(true)] out AnchorlessIdentityData? data)
    {
        data = ent.Comp.LearnedIdentities.FirstOrDefault(x => x.StoredIdentity == stored);
        return data != null;
    }
}
