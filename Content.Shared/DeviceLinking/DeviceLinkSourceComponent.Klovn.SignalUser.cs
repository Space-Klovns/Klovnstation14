namespace Content.Shared.DeviceLinking;

public sealed partial class DeviceLinkSourceComponent
{
    /// <summary>
    ///     Who last sent a signal from this device by using it, or null if its last signal was nobody's doing. A
    ///         signal names the device that sent it, not the person, so whatever credits what a signal sets off
    ///         follows the device back to them through this.
    /// </summary>
    /// <remarks>
    ///     Not saved: a map does not remember who pressed its buttons.
    /// </remarks>
    /// <seealso cref="SharedDeviceLinkSystem.KsInvokePortAsUser"/>
    [ViewVariables]
    public EntityUid? KsSignalUser;
}
