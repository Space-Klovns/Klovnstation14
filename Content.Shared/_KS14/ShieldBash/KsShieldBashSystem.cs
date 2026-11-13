using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Jittering;
using Content.Shared.Repairable;
using Content.Shared.Timing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared._KS14.ShieldBash;

public sealed partial class KsShieldBashSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedChatSystem _chatSystem = default!;
    [Dependency] private SharedJitteringSystem _jitteringSystem = default!;
    [Dependency] private UseDelaySystem _useDelaySystem = default!;
    [Dependency] private ItemToggleSystem _itemToggleSystem = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private EntityQuery<MeleeWeaponComponent> _meleeWeaponQuery = default!;

    /// <summary>
    ///     The shield's own delay, apart from the default one an energy shield's toggle uses.
    /// </summary>
    public const string BashDelayId = "ksShieldBash";

    // After repairs, so a welder still mends a damaged shield instead of banging on it.
    [SubscribeLocalEvent(after: [typeof(RepairableSystem)])]
    private void OnInteractUsing(Entity<KsShieldBashComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled ||
            !CanBashWith(entity, args.Used) ||
            // An energy shield has to be on before it rings.
            !_itemToggleSystem.IsActivated(entity.Owner))
            return;

        args.Handled = true;

        _useDelaySystem.SetLength(entity.Owner, entity.Comp.Cooldown, BashDelayId);
        if (!_useDelaySystem.TryResetDelay(entity.Owner, checkDelayed: true, id: BashDelayId))
            return;

        _audioSystem.PlayPredicted(entity.Comp.BashSound, entity.Owner, args.User);
        _jitteringSystem.DoJitter(args.User, entity.Comp.JitterTime, refresh: true, entity.Comp.JitterAmplitude, entity.Comp.JitterFrequency);

        // Chat is formatted on the server, like every other emote.
        if (_netManager.IsServer)
        {
            _chatSystem.TrySendInGameICMessage(args.User,
                Loc.GetString("ks-shield-bash-emote", ("shield", entity.Owner), ("weapon", args.Used)),
                InGameICChatType.Emote,
                ChatTransmitRange.Normal,
                ignoreActionBlocker: true);
        }
    }

    /// <summary>
    ///     tgstation only lets <c>/obj/item/melee</c> bash: batons and blades, not tools or other shields. There is
    ///         no such line to draw here, where a baseball bat, a crowbar and every sword are tools: anything with
    ///         a melee attack bashes, other than another shield.
    /// </summary>
    private bool CanBashWith(Entity<KsShieldBashComponent> entity, EntityUid usedUid)
    {
        if (usedUid == entity.Owner ||
            !_meleeWeaponQuery.HasComp(usedUid) ||
            HasComp<KsShieldBashComponent>(usedUid))
            return false;

        return entity.Comp.Whitelist == null || _whitelistSystem.IsWhitelistPass(entity.Comp.Whitelist, usedUid);
    }
}
