using Content.Server.Light.Components;
using Content.Shared.Light.Components;

namespace Content.Server._KS14.Light;

/// <summary>
///     Switches the server's copy of an expendable light on while it burns, with the radius and energy from its
///         <see cref="KsExpendableLightMirrorComponent"/>, and off again once it is spent. Called by the expendable
///         light system on every change of state, since it raises nothing of its own.
/// </summary>
public sealed partial class KsExpendableLightMirrorSystem : EntitySystem
{
    [Dependency] private SharedPointLightSystem _pointLightSystem = default!;

    [Dependency] private EntityQuery<KsExpendableLightMirrorComponent> _mirrorQuery = default!;

    public void MirrorState(Entity<ExpendableLightComponent> expendableLightEntity)
    {
        if (!_mirrorQuery.TryComp(expendableLightEntity, out var mirrorComponent))
            return;

        switch (expendableLightEntity.Comp.CurrentState)
        {
            case ExpendableLightState.Lit:
                Mirror(expendableLightEntity, true, mirrorComponent.LitRadius, mirrorComponent.LitEnergy);
                break;
            case ExpendableLightState.Fading:
                Mirror(expendableLightEntity, true, mirrorComponent.FadingRadius, mirrorComponent.FadingEnergy);
                break;
            default:
                Mirror(expendableLightEntity, false, null, null);
                break;
        }
    }

    private void Mirror(EntityUid uid, bool enabled, float? radius, float? energy)
    {
        if (!_pointLightSystem.TryGetLight(uid, out var lightComponent))
            return;

        if (radius is { } newRadius)
            _pointLightSystem.SetRadius(uid, newRadius, comp: lightComponent);

        if (energy is { } newEnergy)
            _pointLightSystem.SetEnergy(uid, newEnergy, comp: lightComponent);

        _pointLightSystem.SetEnabled(uid, enabled, comp: lightComponent);
    }
}
