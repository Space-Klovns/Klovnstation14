using Content.Shared.DeviceNetwork;

namespace Content.Shared.DeviceLinking;

public abstract partial class SharedDeviceLinkSystem
{
    /// <summary>
    ///     <see cref="InvokePort"/> for a signal sent by using the device: remembers <paramref name="user"/> on it
    ///         (<see cref="DeviceLinkSourceComponent.KsSignalUser"/>) as who answers for what the signal sets off.
    /// </summary>
    /// <remarks>
    ///     The user is kept on the device rather than handed to what receives the signal as the user of its trigger:
    ///         a trigger's user is whose client is taken to have predicted its sounds already, and no client predicts
    ///         a signal, so they would hear nothing of what they set off. It also holds however late the signal
    ///         arrives, over the device network or after a timer.
    /// </remarks>
    public void KsInvokePortAsUser(
        EntityUid? user,
        EntityUid uid,
        string port,
        NetworkPayload? data = null,
        DeviceLinkSourceComponent? sourceComponent = null)
    {
        if (Resolve(uid, ref sourceComponent, logMissing: false))
            sourceComponent.KsSignalUser = user;

        InvokePort(uid, port, data, sourceComponent);
    }
}
