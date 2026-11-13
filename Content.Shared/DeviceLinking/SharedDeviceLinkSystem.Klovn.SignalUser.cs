using Content.Shared.DeviceNetwork;

namespace Content.Shared.DeviceLinking;

public abstract partial class SharedDeviceLinkSystem
{
    /// <summary>
    ///     Who pressed the button whose signal is being delivered right now, or null outside of that.
    /// </summary>
    /// <remarks>
    ///     A signal names the device that sent it, not the person who used the device. Anything that reacts to a
    ///         signal by setting off an explosive reads this to credit whoever caused it.
    /// </remarks>
    [ViewVariables]
    public EntityUid? KsSignalUser { get; private set; }

    /// <summary>
    ///     <see cref="InvokePort"/> for a signal a player sent: everything it sets off synchronously can credit
    ///         <paramref name="user"/>.
    /// </summary>
    public void KsInvokePortAsUser(
        EntityUid? user,
        EntityUid uid,
        string port,
        NetworkPayload? data = null,
        DeviceLinkSourceComponent? sourceComponent = null)
    {
        var previousUser = KsSignalUser;
        KsSignalUser = user ?? previousUser;

        try
        {
            InvokePort(uid, port, data, sourceComponent);
        }
        finally
        {
            KsSignalUser = previousUser;
        }
    }
}
