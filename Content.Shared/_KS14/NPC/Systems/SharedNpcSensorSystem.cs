using Robust.Shared.Map;

namespace Content.Shared._KS14.NPC.Systems;

public abstract class SharedNpcSensorSystem : EntitySystem
{
    /// <summary>
    ///     Does nothing on client.
    /// </summary>
    public virtual void DoDisturbance(EntityCoordinates coordinates, float radius) { }
}
