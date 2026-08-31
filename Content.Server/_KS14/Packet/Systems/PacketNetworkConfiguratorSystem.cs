using Content.Server._KS14.Packet.Components;
using Content.Server.Chat.Systems;
using Content.Shared._KS14.Packets.BUI;
using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using NetCord;
using Robust.Server.GameObjects;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Packet.Systems;

/// <summary>
/// This handles...
/// </summary>
public sealed partial class PacketNetworkConfiguratorSystem : EntitySystem
{
    [Dependency] private PacketSystem _packetSystem = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private SharedPopupSystem _sharedPopupSystem = default!;
    [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;


    public override void Initialize()
    {
        SubscribeLocalEvent<PacketNetworkConfiguratorComponent, UseInHandEvent>(OnUse);
        SubscribeLocalEvent<PacketNetworkConfiguratorComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<PacketNetworkConfiguratorComponent, GetVerbsEvent<AlternativeVerb>>(OnAltInteract);
        SubscribeLocalEvent<PacketNetworkConfiguratorComponent, GetVerbsEvent<AlternativeVerb>>(OnAltInteractExecutor);
    }

    private void OnUse(Entity<PacketNetworkConfiguratorComponent> ent, ref UseInHandEvent ev)
    {
        _chatSystem.TrySendInGameICMessage(ent,
            _packetSystem.CreateNetwork([..ent.Comp.Addresses],
            ent.Comp.Frequency),
            InGameICChatType.Whisper,
            false);
    }

    private void OnInteract(Entity<PacketNetworkConfiguratorComponent> ent, ref AfterInteractEvent ev)
    {
        if (ev.Target is not { } target)
            return;

        if (TryComp<PaperComponent>(target, out var paper))
        {
            TryReadPaper(ent, paper);
            return;
        }

        if (!TryComp<PacketNetworkComponent>(target, out var packetNetwork)
            || _packetSystem.GetFrequency(packetNetwork.Frequency) != ent.Comp.Frequency)
            return;

        if (ent.Comp.Mode == ConfiguratorMode.Probe)
            OnProbeInteract(ent, (target, packetNetwork));
        else
            OnSaveInteract(ent, (target, packetNetwork), ev.User);
    }

    private void OnAltInteract(Entity<PacketNetworkConfiguratorComponent> ent, ref GetVerbsEvent<AlternativeVerb> ev)
    {
        if (ev.Target == ent.Owner)
            return;

        var verb = new AlternativeVerb()
        {
            Text = Loc.GetString("packet-configurator-switch-mode"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/in.svg.192dpi.png")),
            Act = () => { ent.Comp.Mode = 1 - ent.Comp.Mode; }
        };

        ev.Verbs.Add(verb);
    }
    private void OnAltInteractExecutor(Entity<PacketNetworkConfiguratorComponent> ent, ref GetVerbsEvent<AlternativeVerb> ev)
    {
        var target = ev.Target;
        var user = ev.User;

        if (!HasComp<ExecutorComponent>(target))
            return;

        var verb = new AlternativeVerb()
        {
            Text = Loc.GetString("packet-configurator-open-executor"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/in.svg.192dpi.png")),
            Act = () => { _userInterfaceSystem.TryOpenUi(target, ExecutorUiKey.Key, user); }
        };

        ev.Verbs.Add(verb);
    }

    private void OnProbeInteract(Entity<PacketNetworkConfiguratorComponent> ent, Entity<PacketNetworkComponent> target)
    {
        _chatSystem.TrySendInGameICMessage(ent,
            Loc.GetString("packet-configurator-probe-address", ("address", target.Comp.Address)),
            InGameICChatType.Whisper,
            false);
    }

    private void OnSaveInteract(Entity<PacketNetworkConfiguratorComponent> ent, Entity<PacketNetworkComponent> target, EntityUid user)
    {
        _sharedPopupSystem.PopupEntity(Loc.GetString("packet-configurator-save"), user, user);
        ent.Comp.Addresses.Add(target.Comp.Address);
    }

    private void TryReadPaper(Entity<PacketNetworkConfiguratorComponent> ent, PaperComponent paper)
    {
        if (!int.TryParse(paper.Content, out var freq))
            return;

        ent.Comp.Frequency = freq;
    }
}
