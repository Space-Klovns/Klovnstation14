using Content.Shared._KS14.ReadyManifest;

namespace Content.Client._KS14.ReadyManifest;

public sealed class ReadyManifestSystem : SharedReadyManifestSystem
{
    public void RequestReadyManifest()
    {
        RaiseNetworkEvent(new RequestReadyManifestMessage());
    }
}
