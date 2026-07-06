using Content.Server.DeviceLinking.Systems;
using Content.Shared.MNET.CardReader;

namespace Content.Server.MNET.CardReader;

public sealed partial class DurationSignalAccessReaderSystem : SharedDurationSignalAccessReaderSystem
{
    [Dependency] private DeviceLinkSystem _deviceLinkSystem = default!;

    public override void ReaderFailed(Entity<DurationSignalAccessReaderComponent> reader, EntityUid user)
    {
        base.ReaderFailed(reader, user);
        _deviceLinkSystem.InvokePort(reader.Owner, reader.Comp.FailurePort);
    }

    public override void ReaderSuccess(Entity<DurationSignalAccessReaderComponent> reader, EntityUid user)
    {
        base.ReaderSuccess(reader, user);
        _deviceLinkSystem.InvokePort(reader.Owner, reader.Comp.SuccessPort);
    }
}
