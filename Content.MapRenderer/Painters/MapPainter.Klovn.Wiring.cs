using System.Linq;
using System.Threading.Tasks;
using Content.Server.Power.Components;
using Content.Shared.SubFloor;
using Robust.Shared.GameObjects;

namespace Content.MapRenderer.Painters;

public sealed partial class MapPainter
{
    private async Task PrepareNativeAppearance(bool revealWires)
    {
        var pair = _pair!;
        var cableIds = pair.Server.EntMan.GetEntities().Where(uid => revealWires && pair.Server.EntMan.HasComponent<CableComponent>(uid))
            .Select(uid => pair.Server.EntMan.GetNetEntity(uid)).ToArray();
        await pair.Client.WaitPost(() =>
        {
            var appearance = pair.Client.System<Robust.Client.GameObjects.AppearanceSystem>();
            foreach (var netId in cableIds)
            {
                var uid = pair.Client.EntMan.GetEntity(netId);
                if (pair.Client.EntMan.HasComponent<SubFloorHideComponent>(uid))
                    appearance.SetData(uid, SubFloorVisuals.ScannerRevealed, true);
            }
            // The headless integration loop advances simulation ticks, not graphical frame
            // updates. Run the native appearance pass before consuming its sprite layers.
            appearance.FrameUpdate(0f);
        });
    }
}
