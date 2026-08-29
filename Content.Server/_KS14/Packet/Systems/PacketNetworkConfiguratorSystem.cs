using Content.Server._KS14.Packet.Components;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using NetCord;
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


    public override void Initialize()
    {
        SubscribeLocalEvent<PacketNetworkConfiguratorComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<PacketNetworkConfiguratorComponent, GetVerbsEvent<AlternativeVerb>>(OnAltInteract);
    }

    private void OnUse(Entity<PacketNetworkConfiguratorComponent> ent, ref UseInHandEvent ev)
    {
        _sharedPopupSystem.PopupEntity(_packetSystem.CreateNetwork([..ent.Comp.Addresses],
            ent.Comp.Frequency),
            ev.User,
            ev.User,
            PopupType.LargeCaution);
    }

    private void OnInteract(Entity<PacketNetworkConfiguratorComponent> ent, ref AfterInteractEvent ev)
    {
        if (ev.Target is not { } target
            || !TryComp<PacketNetworkComponent>(target, out var packetNetwork)
            || _packetSystem.GetFrequency(packetNetwork.Frequency) != ent.Comp.Frequency)
            return;

        if (ent.Comp.Mode == ConfiguratorMode.Probe)
            OnProbeInteract(ent, (target, packetNetwork));
        else
            OnSaveInteract(ent, (target, packetNetwork), ev.User);
    }

    private void OnAltInteract(Entity<PacketNetworkConfiguratorComponent> ent, ref GetVerbsEvent<AlternativeVerb> ev)
    {
        var verb = new AlternativeVerb()
        {
            Text = Loc.GetString("packet-configurator-switch-mode"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/in.svg.192dpi.png")),
            Act = () => { ent.Comp.Mode = 1 - ent.Comp.Mode; }
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
}
