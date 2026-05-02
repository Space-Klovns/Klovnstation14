using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;

namespace Content.Shared._KS14.DeviceLinkVisuals;

public sealed class DeviceLinkVisualsSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DeviceLinkVisualsComponent, NewLinkEvent>(OnConnected);
        SubscribeLocalEvent<DeviceLinkVisualsComponent, PortDisconnectedEvent>(OnDisconnected);

        SubscribeLocalEvent<DeviceLinkVisualsComponent, MapInitEvent>(OnMapInit);
    }

    private void OnConnected(Entity<DeviceLinkVisualsComponent> entity, ref NewLinkEvent args)
    {
        _appearanceSystem.SetData(entity.Owner, DeviceLinkVisuals.Connected, true);
    }

    private void OnDisconnected(Entity<DeviceLinkVisualsComponent> entity, ref PortDisconnectedEvent args)
    {
        Update(entity);
    }

    private void OnMapInit(Entity<DeviceLinkVisualsComponent> entity, ref MapInitEvent args)
    {
        Update(entity);
    }

    private void Update(Entity<DeviceLinkVisualsComponent> entity)
    {
        var hasSourceConnected = TryComp<DeviceLinkSourceComponent>(entity, out var sourceComponent) &&
            sourceComponent.Outputs.Count != 0;

        var hasSinkConnected = TryComp<DeviceLinkSinkComponent>(entity, out var sinkComponent) &&
            sinkComponent.LinkedSources.Count != 0;

        _appearanceSystem.SetData(entity.Owner, DeviceLinkVisuals.Connected, hasSourceConnected || hasSinkConnected);
    }
}
